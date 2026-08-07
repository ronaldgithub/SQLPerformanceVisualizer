/* ============================================================================
   JOIN ORDER (BUILD/PROBE, OUTER/INNER) & PHYSICAL OPERATOR SELECTION
   StackOverflow2013 demo script
   ============================================================================

   All queries below join a small, arbitrarily-bounded slice of Badges
   (Id BETWEEN 1 AND 5000, out of a few million rows) against the full Users
   table. That size gap makes the "right" choice obvious to a human, which is
   exactly what makes it a good demo: we force each wrong answer with join
   hints and watch cost/memory grant get worse, then compare against the
   optimizer's own free choice.

   HOW TO INSPECT EACH PLAN (SSMS):
     Ctrl+M (actual plan) or Ctrl+L (estimated plan), run/parse the statement,
     then:
       - Click the leftmost (SELECT) icon, press F4 -> "Estimated Subtree
         Cost" and (on actual plans) "Memory Grant" info.
       - Hover/click the Hash Match icon -> tooltip shows "Hash Keys Build"
         vs "Hash Keys Probe". Convention: top input edge = build, bottom
         input edge = probe.
       - Hover/click the Nested Loops icon -> top input edge = outer, bottom
         input edge = inner. The inner side's seek/scan operator will show a
         high "Number of Executions" (actual plan) equal to the outer row count.

   CAUTION: B2 (i.e. B5 below) and B4 (B7 below) deliberately force an
   inefficient plan (large table driving/building). Use the ESTIMATED plan
   (Ctrl+L) for those rather than running them for real -- B7 in particular
   asks the engine to loop over the entire Users table.
   ============================================================================ */

USE StackOverflow2013;
GO

-- ----------------------------------------------------------------------------
-- SETUP: demo index (safe to re-run)
-- Gives Badges a sort order on UserId that matches the Users clustered index
-- order on Id, so the MERGE JOIN demo doesn't need an extra explicit Sort.
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Demo_Badges_UserId' AND object_id = OBJECT_ID('dbo.Badges'))
    CREATE INDEX IX_Demo_Badges_UserId ON dbo.Badges(UserId);
GO


-- ----------------------------------------------------------------------------
-- B0. FREE CHOICE: no hints -- see what the optimizer picks on its own.
--     Expect Nested Loops, Badges-slice driving (outer), Users probed via its
--     clustered PK. This is the baseline to compare every forced variant to.
-- ----------------------------------------------------------------------------
SELECT u.DisplayName, b.Name, b.Date
FROM dbo.Badges b
JOIN dbo.Users u ON u.Id = b.UserId
WHERE b.Id BETWEEN 1 AND 5000
OPTION (RECOMPILE);


/* ----------------------------------------------------------------------------
   B1-B3: PHYSICAL OPERATOR SELECTION -- same logical query, operator forced
   via join hint each time. Compare the SELECT operator's "Estimated Subtree
   Cost" (F4 on the leftmost icon) across all three plus B0 above.
   ---------------------------------------------------------------------------- */

-- B1. Force LOOP: should look close to B0's free choice and cost -- this is
--     the operator the optimizer picked anyway
SELECT u.DisplayName, b.Name, b.Date
FROM dbo.Badges b
INNER LOOP JOIN dbo.Users u ON u.Id = b.UserId
WHERE b.Id BETWEEN 1 AND 5000
OPTION (RECOMPILE);

-- B2. Force HASH: builds a hash table from one side and probes with the
--     other -- check the Hash Match operator's tooltip/properties for which
--     side became "Build" vs "Probe", and compare Memory Grant to B1 (which
--     needs none)
SELECT u.DisplayName, b.Name, b.Date
FROM dbo.Badges b
INNER HASH JOIN dbo.Users u ON u.Id = b.UserId
WHERE b.Id BETWEEN 1 AND 5000
OPTION (RECOMPILE);

-- B3. Force MERGE: both inputs need to arrive in join-key order. Thanks to
--     IX_Demo_Badges_UserId and Users' clustered PK on Id, this should be a
--     genuine ordered merge with no extra Sort operator (verify there isn't
--     one in the plan -- if there is, the optimizer decided sorting was
--     cheaper than reading the new index, which is itself an interesting
--     result)
SELECT u.DisplayName, b.Name, b.Date
FROM dbo.Badges b
INNER MERGE JOIN dbo.Users u ON u.Id = b.UserId
WHERE b.Id BETWEEN 1 AND 5000
OPTION (RECOMPILE);


/* ----------------------------------------------------------------------------
   B4-B7: JOIN ORDER / BUILD-PROBE / OUTER-INNER -- FORCE ORDER pins the
   syntactic table order as the join order, so which table is listed first
   determines which side becomes build (hash) or outer (loop). Flipping the
   FROM clause order between each pair flips that role with nothing else
   changed -- this isolates the "which side" decision from the "which
   operator" decision above.
   ---------------------------------------------------------------------------- */

-- B4. HASH, Badges-slice (small) listed first -> becomes the BUILD input.
--     Cheap: small hash table built, Users streamed through as the probe.
SELECT u.DisplayName, b.Name, b.Date
FROM dbo.Badges b
INNER HASH JOIN dbo.Users u ON u.Id = b.UserId
WHERE b.Id BETWEEN 1 AND 5000
OPTION (FORCE ORDER, RECOMPILE);

-- B5. HASH, same query, Users (large) listed first -> forced to become the
--     BUILD input instead. Same logical result, deliberately worse: the
--     optimizer now has to build a hash table sized for millions of Users
--     rows. Compare Memory Grant and Estimated Subtree Cost against B4.
--     >>> Use Ctrl+L (Estimated Plan) for this one, don't execute it. <<<
SELECT u.DisplayName, b.Name, b.Date
FROM dbo.Users u
INNER HASH JOIN dbo.Badges b ON u.Id = b.UserId
WHERE b.Id BETWEEN 1 AND 5000
OPTION (FORCE ORDER, RECOMPILE);

-- B6. LOOP, Badges-slice listed first -> becomes the OUTER input. Efficient:
--     ~5000 outer iterations, each doing one clustered-index seek into Users.
SELECT u.DisplayName, b.Name, b.Date
FROM dbo.Badges b
INNER LOOP JOIN dbo.Users u ON u.Id = b.UserId
WHERE b.Id BETWEEN 1 AND 5000
OPTION (FORCE ORDER, RECOMPILE);

-- B7. LOOP, same query, Users listed first -> Users is forced to be the
--     OUTER input, meaning one iteration (and one seek/scan against the
--     Badges index) per Users row -- millions of iterations instead of 5000.
--     Same result set, vastly different cost, purely from swapping outer/inner.
--     >>> Use Ctrl+L (Estimated Plan) for this one -- do not execute it. <<<
SELECT u.DisplayName, b.Name, b.Date
FROM dbo.Users u
INNER LOOP JOIN dbo.Badges b ON u.Id = b.UserId
WHERE b.Id BETWEEN 1 AND 5000
OPTION (FORCE ORDER, RECOMPILE);


/* ============================================================================
   TEARDOWN (optional) -- run when you're done experimenting
   ============================================================================
DROP INDEX IF EXISTS IX_Demo_Badges_UserId ON dbo.Badges;
*/