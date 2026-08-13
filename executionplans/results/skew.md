## Execution Plan Analysis: skew.sqlplan

**This is an actual plan** (DOP 8, runtime stats present, CE model 170, `OPTION (RECOMPILE)`), so timing claims below are measurements, not guesses.

### What's actually slow

The query takes **30,997 ms elapsed / 29,163 ms CPU** total (`QueryTimeStats`), and **28,095 ms of that — 91% — is self elapsed time on node 6, the Index Spool (Eager Spool)** (TOP OPERATORS BY SELF ELAPSED TIME). Its child, node 8's Table Scan of `dbo.VotesSkewed`, contributes another 2,505 ms self elapsed. Together nodes 6+8 account for ~30.6 s of the 31 s total — everything else (the Nested Loop, the Users scan, the TopN Sort) is noise (≤ 28 ms self elapsed each).

Node 6's per-thread stats explain *why* it's so expensive:

| thread | rows | elapsed | CPU |
|---|---|---|---|
| 1–7 | 0 | ~27,950 ms each | 50–200 ms each |
| 8 | 948 | 27,978 ms | 27,977 ms |

Only thread 8 does any real work; threads 1–7 sit for ~27,950 ms each doing almost nothing. That matches `TOP WAITS`: **EXECSYNC = 195,251 ms across 7 waits** (7 × ~27,900 ms). An eager index spool must be **fully built before any probe of it can start**, and the build itself is not parallelized — one worker scans all of `dbo.VotesSkewed` (node 8: 10,144,245 rows, matching the 10,144,200 estimate almost exactly) and inserts every row into a temporary spool structure. The other seven DOP‑8 workers block on `EXECSYNC` waiting for that single-threaded build to finish. This is the entire query: a parallel plan that pays full coordination overhead for a plan that runs serially anyway.

Once built, the spool is cheap to use — node 6 shows 299,398 executions (once per outer-apply row) emitting only 948 rows total (0.003 rows/exec), so the actual per-row probing after the build is essentially free.

### Why the optimizer built a spool at all

Node 6's seek predicate is:

```
Prefix:     VotesSkewed.UserId EQ Users.Id
StartRange: VotesSkewed.VoteTypeId GE 1
EndRange:   VotesSkewed.VoteTypeId LE 4
```

That's exactly the shape the `OUTER APPLY`'s correlated subquery needs (`v.UserId = u.Id AND v.VoteTypeId BETWEEN 1 AND 4 ORDER BY v.CreationDate DESC`). There is no index on `dbo.VotesSkewed` that supports a seek on `(UserId, VoteTypeId)`, so instead of seeking 299,398 times against a real index, the optimizer built one itself at runtime via the eager spool — at the cost of first materializing the whole 10.1M-row table.

### What I ruled out

- **Not a memory grant problem** — granted 1,024 KB, used only 152 KB (14.8%), `GrantWaitTime = 0`.
- **Not a cardinality estimation problem in the sense of "bad row estimate → bad plan choice."** Node 6 is overestimated 67.3x per execution (est 66 vs actual 0.0032) and node 1 is overestimated 40x, but these are a symptom of the spool having no real statistics, not the cause of the slowness — even a perfectly-estimated spool still has to be built serially before any row can flow.
- **High CPU (29,163 ms) vs. elapsed (30,997 ms) is not itself a red flag** — that's normal for a parallel plan; the actual problem is the opposite (threads *not* using their CPU, sitting on EXECSYNC instead).
- **`CXPACKET` (21,536 ms / 8,925 waits)** is ordinary parallel coordination overhead, dwarfed by and largely a byproduct of the EXECSYNC stall — not a separate issue.
- **No missing-index hint is present**, but the digest's note applies here: an eager spool suppresses that hint. `(none)` does not mean no index is needed.

### Recommendation

Add a supporting index on the base table, built from the spool's own seek predicate (node 6):

```sql
CREATE INDEX IX_VotesSkewed_UserId_VoteTypeId
ON dbo.VotesSkewed (UserId, VoteTypeId)
INCLUDE (CreationDate);
```

Mechanism: with this index, the Nested Loop (node 1) can seek `VotesSkewed` directly per `Users.Id`, the same way node 6 currently seeks the spool — but against a real, pre-built structure. That removes the need for the optimizer to build an eager spool at all, which removes both the single-threaded 10.1M-row scan/build (nodes 8 and 6, ~30.6 s combined) and the resulting `EXECSYNC` stall on the other 7 workers. The `TOP (5) ... ORDER BY CreationDate DESC` sort (node 4) would remain — it's already negligible (47 ms self elapsed total across 299k invocations) since it only ever sorts one user's small set of matching votes, not the whole table.
