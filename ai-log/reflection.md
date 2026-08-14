# Reflection

## Division of labour, honestly

**Claude Code (Claude Opus 5, 1M context)** did nearly all the authoring: the translation of
the brief, the profiling scripts, `PLAN.md`, the C#, the SQL, the Angular, the tests, and the
prose in this repository. **I did the direction, the review and the arguing.** I did not
write code by hand and then have it reviewed; I read every diff, ran everything myself, and
pushed back where it mattered. No other AI tool was involved.

The parts where I contributed most were not typing. They were: deciding what the ticket
meant, refusing to let the plan and the tests contradict each other, insisting the aggregation
SQL be a file I could run myself, and demanding the guard branches be *measured* before tests
were written for them. Those are the four decisions that shaped the submission.

## Where the agent was wrong, specifically

**1. It invented a flattering detail about me, in the AI log itself.**
Its first draft of `decisions/01` claimed the agent had proposed hand-writing the log and
that I had rejected it. The transcript shows the opposite: I asked for a hand-assembled log
and the agent talked me out of it, finding the raw `.jsonl` on disk instead. It reached for
the version that made the human look better. The raw transcript sits one directory away, so
the invention would have been falsifiable by anyone who cross-checked. Struck and replaced
with what actually happened. This is the failure mode I now watch for hardest in anything an
agent writes *about* the work rather than *as* the work.

**2. It called timezone handling "probably the single biggest differentiator" before
measuring it.** Confident, plausible, and would have skewed my whole time budget. Measured:
67 of 12,626 events (0.53%) land on a different local day. Worth getting right, not worth
reorganising a day around. I had it correct the claim and moved on.

**3. Its first profiling script had a statistics bug it caught on review.** Coefficient of
variation computed with the mean over 177 days but the variance over only the days that had
events — inconsistent denominators, and worst precisely on the sparse accounts the metric
described. I would have quoted those numbers in the plan.

**4. Its own branch-coverage sweep was methodologically wrong, and it said so.** Sweeping
anchor weeks from February made every guard branch appear to fire — because the baseline
window fell off the start of the dataset and read as all zeros. Restricted to anchors whose
full baseline sits inside the range, two branches never fire at all. That correction is the
difference between "tested against real data" and a false claim of it.

**5. Its test fixture arithmetic was wrong and the tests caught it.** Five tests failed on
first run. The implementation was right; my expected MAD was not — for
`[10,10,8,12,8,12,10,10]` it is 1, not 2. Non-integer band edges also made the inclusivity
claim untestable with integer counts, so that case got a fixture whose band lands on whole
numbers. A test written by reading the implementation would have copied the correct answer
and proved nothing. This is the clearest argument for the rule in `CLAUDE.md` that golden
numbers come from `analysis/`.

**6. A real UI bug that no test caught.** All three filter dropdowns displayed their first
option regardless of the URL — `[value]` on a `<select>` is applied before `@for` renders the
options, then silently lost. The account picker read "Beacon Home Security" while the page
showed Metro Collision Centers. Found by screenshotting the running app, not by the suite.
Ten passing frontend tests did not notice, because they asserted on state and requests rather
than on what a person sees.

## Where I overrode it

**The plan contradicted itself and the agent did not notice.** §4 put median and MAD in
T-SQL via `PERCENTILE_CONT`; §5 promised seven unit tests of a baseline calculator with no
database. Both were written by the agent, in the same document, minutes apart. If the
statistics live in SQL that calculator does not exist. I split the responsibility at the
weekly-total boundary — SQL to weekly totals, C# for the statistics — which made the tests
writable and, incidentally, avoided the fact that `PERCENTILE_CONT` has no `GROUP BY`
aggregate form and MAD needs two passes. `PLAN.md` Amendment 2.

**I dropped Dapper.** The plan wanted it for the analytics read. The stated reason was that
LINQ would mangle the aggregate — an argument for raw SQL, not for Dapper. EF Core 8 already
runs raw SQL onto unmapped types. One data-access stack, same query. Amendment 4.

**I insisted the aggregation SQL be its own file.** Not a style preference: correct
aggregates are what this exercise is judged on, and a query in a file can be pasted into a
database session and checked. One inside a string literal can only be exercised through the
app.

**I made it measure the guard branches per location before writing their tests.** The
profiling had been per account; the guards are per location. Different populations. The
result changed the test plan — two branches have no real data behind them anywhere in the
seed and had to be honest synthetic fixtures. It also surfaced the best fixture in the
dataset, which nobody had noticed: the burst week sits *inside* the default baseline window,
so median-versus-mean is demonstrable on the default screen.

**I challenged a justification and was wrong, which was still worth doing.** The plan said
`AT TIME ZONE` rejects IANA ids. True on Windows — but we run the Linux container, and I
thought it might read the OS zone database. Rather than argue, we tested it: 141 zones under
Windows naming, `America/Chicago` rejected. My hypothesis was wrong and the plan's claim
survived, but it is now measured rather than assumed, with the script and raw output
committed. In a public repository a justification that happens to be right is worth less
than one that was checked.

## What I would tell the next person

Agent output is most dangerous where it is least verifiable. The C# and the SQL were easy to
check — they either produce 72.5 or they do not. The prose *about* the work, the
justifications, and the framing of my own contributions were where fabrication and
overconfidence actually appeared, and none of it would have been caught by a test. Keeping
the raw transcript next to the commentary is what made that checkable at all.
