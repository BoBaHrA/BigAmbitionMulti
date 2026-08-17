# Competitive Suite — First Field Test

Goal: validate the maximum amount of multiplayer + competitive behavior in **one two-player session**, so the second player is not repeatedly asked to reinstall/rejoin for one-off checks.

## Before involving the second player — solo smoke test

Do this on the host machine first.

1. Build the `feature/competitive-dashboard` branch against the same Big Ambitions EA 0.11 installation that will be used for the field test.
2. Launch the game with **one** copy of the mod installed (avoid Workshop + ModsLocal duplicate installs).
3. Confirm the existing **Business** app still opens.
4. Confirm the new **Competition** top-bar app appears and opens.
5. Confirm the new **Market Intel** top-bar app appears and opens.
6. Open/close Business → Competition → Market Intel → a native app several times.
   - Correct title at the top.
   - Correct selected-dot state.
   - No stuck invisible page.
   - Movement/input returns normally after closing the menu.
7. Leave Competition open for ~20 seconds.
   - No obvious frame-time spikes.
   - Log should contain `[CompetitivePerf]` only once for the dashboard cadence notice.
8. Leave Market Intel open for ~20 seconds.
   - No obvious frame-time spikes.
   - Log should contain `[MarketIntel] DIAG ...` and one `[CompetitivePerf]` cadence notice.
9. Use the mod's bug-report button once and inspect `report.md`.
   - It should contain a `## Competitive Diagnostics` section.
   - In solo/offline context it is okay for the report to show one human and no direct price overlaps.

If any of the above fails, fix it **before** asking Player 2 to join.

---

## Two-player test setup

Use the same:

- Big Ambitions game build
- mod commit/build
- protocol version
- other gameplay/content mods where possible

For the cleanest first signal, preferably test with no unrelated gameplay/content mods enabled.

Roles:

- **P1:** host
- **P2:** client

Do not start with mergers. The first competitive pass must prove that two fully independent companies remain independent.

---

# Field run

## Phase A — connect and world identity

1. P1 hosts a **new** multiplayer game.
2. P2 joins through the intended real connection path (Steam relay preferred if that is how the session will normally be played).
3. Both finish loading before doing anything important.
4. Verify on both machines:
   - both player avatars exist;
   - both can move;
   - same game clock;
   - same weather;
   - chat works;
   - each player has their own starting money;
   - neither player can spend the other player's personal balance.
5. Open **Competition** on both machines.
   - two human rows should resolve;
   - names should match;
   - local player should not disappear from their own view.
6. Open **Market Intel** on both machines.
   - diagnostics should show `humans=2` / two humans;
   - stats may still be mostly zero before businesses exist; that is valid.

### Evidence checkpoint A

If anything is already wrong, create a bug report on **both** machines now before continuing.

---

## Phase B — independent tenancy / companies

1. P1 rents Business A.
2. P2 attempts to rent the **same** location.
   - P2 must be denied/see it unavailable.
   - no duplicate starter items should appear after the denied rent.
3. P2 rents a different Business B.
4. Both create a real business and give it a distinct name.
5. Verify from the other machine:
   - business name is visible;
   - business type is correct;
   - location is attributed to the correct player;
   - neither player accidentally gets `RentedByPlayer` authority over the other's shop.
6. Open Competition again.
   - business counts should become 1 / 1 after the stats refresh path has run.

### Evidence checkpoint B

Open Market Intel on both machines. Its diagnostic line should resolve two player businesses; save screenshots if ownership differs between machines.

---

## Phase C — create a real price war

For this phase **both players must operate the same business type and sell at least one identical product**.

1. Choose one common product, called **Test Product** below.
2. Set both businesses initially to the same price.
3. Wait at least one MP price-sync cycle (~5 seconds).
4. Open Market Intel on both machines.
   - `PRICE WARS` should show Test Product.
   - both sides should agree on both displayed prices.
5. P1 lowers Test Product significantly below P2.
6. Wait >5 seconds.
7. Check both machines:
   - P1's new price is visible in Market Intel;
   - P2's local registration of P1's shop carries the new price;
   - the competitive feed should record the undercut relationship **if it flipped from the baseline**.
8. P2 now undercuts P1.
9. Wait >5 seconds.
10. Check both machines again.
    - the relation should flip;
    - Market Intel should agree about which side is cheaper.

This phase validates the most important premise of the project: player prices are not just decorative — both simulations receive the same competitor price inputs.

### Evidence checkpoint C

Create a bug report on both machines **while both shops and price tables still exist**. The appended Competitive Diagnostics section should list:

- both human players;
- both resolved player businesses;
- priced-business counts;
- at least one `Direct price overlap` for Test Product.

Keep these two reports even if everything works.

---

## Phase D — commerce across players

1. P1 enters P2's shop while it is open.
2. Verify P1 sees the current stock/interior.
3. P1 buys Test Product from P2's shop.
4. Verify:
   - P1 is charged once;
   - P2 receives the correct sale credit once;
   - stock changes do not duplicate;
   - no item appears on only one machine permanently.
5. Reverse the test: P2 buys from P1.

If practical, also have the non-owner work/help at the other shop using the existing permission system, but keep this optional for the first competitive signal.

---

## Phase E — leaderboard / market stats movement

1. Let both businesses trade long enough to produce non-zero weekly-income data.
2. Open Competition on both machines.
3. Verify:
   - same ranking order;
   - roughly the same remote weekly-income values;
   - correct business counts;
   - correct top rival;
   - shared-category leaders make sense;
   - `Share` is treated as the documented weekly-income proxy, not true unit-sales market share.
4. Leave Competition open through at least three momentum samples (>15 seconds) while income changes.
   - local Momentum should move without affecting game state.

Note: Momentum is intentionally local UI history. Two clients opened at different times do **not** have to display identical momentum.

---

## Phase F — business sale / transfer

Only do this after the independent-company state above has been captured.

1. P1 makes/accepts a player-to-player business-sale flow for one test business using the mod's existing Business Hub path.
2. Verify after completion:
   - tenancy transfers to the buyer;
   - seller loses authority;
   - buyer gains authority;
   - interior is not empty;
   - staff transfer;
   - schedule remains usable;
   - payment moves exactly once;
   - Competition business counts eventually reflect the transfer.
3. Open Market Intel and verify the sold address now resolves to the new owner.

---

## Phase G — save, disconnect, reconnect

1. Run a coordinated multiplayer save.
2. Record both balances and each business owner.
3. P2 leaves cleanly.
4. P2 rejoins the same multiplayer session.
5. Verify:
   - P2's character/progress returns;
   - money is sane;
   - business ownership is unchanged;
   - interiors are intact;
   - prices are intact/reassert after sync;
   - Competition resolves both players again;
   - Market Intel resolves the same businesses again.
6. Save once more.

### Final evidence

Create one final bug report on both machines before ending the session.

---

# Pass criteria

The first competitive build is considered field-viable when all of these are true:

- two independent player companies coexist in one city;
- the same building cannot be independently claimed by both;
- each player retains private money outside mergers;
- player businesses/ownership resolve consistently on both machines;
- retail prices propagate to the other player;
- a deliberately created price war is visible consistently;
- cross-player shop commerce does not double-pay/double-charge;
- Competition displays both real players and their business stats;
- Market Intel identifies shared categories / price overlaps when they exist;
- save → disconnect → reconnect preserves the competitive world;
- reports from both machines contain Competitive Diagnostics for postmortem analysis.

# Things deliberately NOT required for this first run

These should not block the first competitive test:

- true historical market-share persistence;
- seasons / victory conditions;
- team-vs-team mode;
- synchronized Competition momentum history;
- competitive notifications while the menu is closed;
- a new network protocol dedicated to the dashboard.

Those are safer to add after the existing economy/ownership/price data has been proven in a real two-machine session.
