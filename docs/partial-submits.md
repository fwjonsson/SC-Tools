# Partial submits (Hauling Planner): test notes

Recorded 22 Oct 2026 (Frank, zzoega). Not built into the planner yet; more testing planned. The same notes are in the claude.ai project
(`claude/partial-submits.md`).

## Data

| Contract | Cargo delivered | aUEC before | aUEC after | Paid | Full reward (rewards table) | Paid / full |
|---|---|---|---|---|---|---|
| 2 To Ruin | 264 / 524 SCU (50.4%) | 43,849,220 | 44,039,220 | 190,000 | 1,268,250 | 15.0% |
| 3 To Ruin | 192 / 551 SCU (34.8%) | 44,039,220 | 44,292,720 | 253,500 | 1,691,000 | 15.0% |
| 3 To Ruin | 192 / 540 SCU (35.6%) | 44,292,720 | 44,546,220 | 253,500 | 1,691,000 | 15.0% |
| 4 To Ruin (master) | 491 / 523 SCU (93.9%) | 55,602,480 | 57,208,728 | 1,606,248 | 2,113,750 | 76.0% |

## What it shows so far

- The payout is NOT proportional to the SCU delivered: 93.9% delivered paid 76.0%; ~35–50% delivered paid 15%.
- The first three (one stop's worth or so) all paid ~15% of the full reward, rounded to 500. The two 3 To Ruin submits delivered different SCU and
  paid exactly the same.
- The master submit was missing only 32 SCU (one 32 box) and lost 24% of the reward (507,502 aUEC, ~15,900 per missing SCU). Its payout is not a
  round number, unlike the 15% ones.
- One model that fits all four (unconfirmed): pay = full reward × (delivered share − about 3 × missing share), but never less than 15%.
  93.9% → 0.939 − 3 × 0.061 ≈ 0.76; at 50% or 35% the formula goes below 15%, so the 15% floor applies.

## Open questions / tests to do

- Test the model with one more point in between, e.g. ~80% delivered: the model predicts about 20% of the reward; ~90% delivered: about 60%.
- How the Game.log writes a partial submit (end state: COMPLETED, FAILED, other?). Needed before the planner can count it (today it would count the
  full reward or treat the cargo as lost). The Debts tab's Payout box is the workaround: type what it really paid.
