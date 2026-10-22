# Feature Specification: Filament ledger and realized cost

**Feature Branch**: `017-filament-ledger-and-cost`

**Created**: 2026-10-22

**Status**: Draft

**Input**: User description: "Filament ledger and realized cost. Three operator-facing capabilities, in priority order. (P1) Spool history: an operator can open any spool and see every change to its remaining weight, newest first. Each row shows when it happened, the change in grams, the weight remaining afterwards, where it came from (a print that used it, linked to that print; a manual usage entry; or a manual weight correction) and an optional reason for corrections. Manual weight corrections are recorded in this history, with an optional reason the operator can type when they change the weight. The spool detail page loads one spool directly instead of the whole inventory. After any print closes, the spools list, printer filament meters and dashboard refresh on their own without a manual reload. (P2) Purchase info and adding several spools at once: when adding spools the operator can set a quantity (1 to 50) to add several identical spools in one step, and can record what was paid for each spool, the supplier, an order number and the received date, all optional. These can be edited later on the spool page together with the label and remaining weight in the existing single Save. Editing a spool's price never changes the recorded cost of past prints. Price paid is a plain number with no currency symbol, and zero is a valid price that is distinct from an unknown price. (P3) Cost of the filament a print used: when a print ends (completed, failed or cancelled; dispatched from the queue or started on the printer) the cost of the filament it used is recorded once per filament line, using the price paid for the spool that fed it when known (price paid divided by the spool's weight, per kg), otherwise the filament colour's price per kg, otherwise the filament's price per kg. Failed and cancelled prints record the cost of only what they actually used. A line with no known price shows as no price and the print's total filament cost is marked partial. A line that was not deducted has no cost. If the operator later assigns a priced spool to a line that had no price, that line gains a cost; a line that already has a recorded price keeps it. The job detail page shows grams and cost per line, a total, a partial marker, and a link to the spool. The existing assembly Build-panel cost estimate is a forecast and keeps using current catalog prices. Out of scope for this feature: low-stock thresholds, stock totals and tiles, finish reasons and restoring finished spools, checks that a spool holds enough filament before dispatch, usage or cost reports and CSV export, RFID or QR spool identification, weight-only bulk stock, price history, and multi-line delivery entry."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See where a spool's filament went (Priority: P1)

An operator opens a spool and sees a history of every change to its remaining weight, newest first. Each entry shows when it happened, how much the weight changed, the weight left afterwards, where the change came from, and an optional reason. An entry that came from a print links to that print. When the operator corrects a spool's weight by hand, the correction appears in the history and they can type a short reason while editing. The spool page shows that one spool without loading the whole inventory. After a print closes, spool lists, spool pages, printer filament meters and the dashboard show the new weights without a manual reload.

**Why this priority**: The system already deducts filament automatically, but nobody can see where a spool's grams went, and a manual correction leaves no trace. Trust in the remaining weight is the base that cost tracking, low-stock alerts and enough-filament checks all depend on.

**Independent Test**: Register a spool, load it, run a print to completion on the virtual printer, then correct the weight by hand with a reason. Open the spool: two entries, newest first, with correct changes and remaining weights. The print entry opens the print. The lists and dashboard updated without a reload when the print closed.

**Acceptance Scenarios**:

1. **Given** a spool used by a completed print, **When** the operator opens the spool, **Then** the history shows an entry for that print with the grams used, the weight remaining afterwards, the print's name, and a link to the print.
2. **Given** a spool with 800 g remaining, **When** the operator saves a corrected weight of 750 g with the reason "weighed on scale", **Then** the history shows a correction entry that decreases the weight by 50 g, leaves 750 g, and shows the reason.
3. **Given** a correction that raises the weight (for example 750 g to 780 g), **When** it is saved, **Then** the entry shows an increase of 30 g.
4. **Given** the operator saves a spool edit that changes only the label, or saves the same weight again, **When** the save completes, **Then** no history entry is added.
5. **Given** a manual usage entry is recorded for a spool, **When** the operator opens the spool, **Then** the history shows it as manual usage.
6. **Given** a spool with no recorded changes, **When** the operator opens it, **Then** the page says nothing has been recorded yet.
7. **Given** history entries recorded before this feature existed, **When** the operator opens the spool, **Then** they appear without a remaining weight, and no remaining weight is invented for them.
8. **Given** the spools list and the dashboard are open, **When** a print reaches a final state, **Then** the remaining weights on screen update without a reload.
9. **Given** a spool with more than 50 history entries, **When** the operator opens it, **Then** the latest 50 show with a line stating how many exist in total.
10. **Given** any history entry, **When** the operator looks for a way to change or remove it, **Then** none is offered.

---

### User Story 2 - Record what a spool cost and add several at once (Priority: P2)

When adding spools, the operator sets a quantity (1 to 50) to add several identical spools in one step. They can record what was paid for each spool, the supplier, an order number, and the received date. All of these are optional. They can edit the same fields later on the spool page, together with the label and remaining weight, in the existing single Save. Changing a spool's price never changes the recorded cost of prints that already used it.

**Why this priority**: Real cost needs a purchase price on the spool itself, since the same colour is bought at different prices over time. Adding ten spools of one delivery is ten form submissions today. This story supplies the price that User Story 3 uses first, but the history in User Story 1 does not depend on it.

**Independent Test**: Add 4 spools of one colour with a price, supplier, order number and received date in one submission. Four separate spools exist with those values. Edit one spool's price and confirm the others are unchanged.

**Acceptance Scenarios**:

1. **Given** the Add spool page, **When** the operator chooses a colour, sets quantity 4, price paid 18.50, a supplier, an order number and a received date, and saves, **Then** four separate spools exist, each with those values, and four registrations appear in the activity log.
2. **Given** quantity is left blank, **When** the operator saves, **Then** one spool is created.
3. **Given** quantity 0 or 51, **When** the operator saves, **Then** the save is refused with a clear message and no spool is created.
4. **Given** price paid is left blank, **When** the spool is saved, **Then** its price is unknown; **Given** price paid is 0, **Then** zero is kept as a real price and shown as 0, not as unknown.
5. **Given** a negative price, **When** the operator saves, **Then** the save is refused with a clear message.
6. **Given** an existing spool, **When** the operator changes price paid, supplier, order number and received date along with the label and remaining weight and saves once, **Then** all of them are kept.
7. **Given** a print already recorded a cost from a spool, **When** the operator edits that spool's price, **Then** the print's recorded cost does not change.
8. **Given** quantity 3 with a label and a remaining weight, **When** the operator saves, **Then** every one of the three spools gets that label and remaining weight.

---

### User Story 3 - See what a print's filament cost (Priority: P3)

When a print ends, completed, failed or cancelled, and whether it was dispatched from the queue or started on the printer, the cost of the filament it used is recorded once for each filament line. The price per kg comes from the spool that fed the line when it has a price (price paid divided by the spool's weight), otherwise from the filament colour's price, otherwise from the filament's price. The job page shows grams and cost per line, a total, a marker when the total is partial, and a link to the spool.

**Why this priority**: It answers "what did this print really cost in filament" and "what did the failed ones cost". It depends on the weight deductions that exist today and benefits from the spool price in User Story 2, but it still works from catalog prices alone.

**Independent Test**: Give one spool a price paid and leave another unpriced under a colour that has a price. Run two prints, one on each, plus one that fails midway. The job pages show the costs described below, and changing any price afterwards leaves them unchanged.

**Acceptance Scenarios**:

1. **Given** a spool with price paid 20.00 and weight 1000 g, **When** a print uses 50 g from it and completes, **Then** the line shows cost 1.00 and says the price came from the spool.
2. **Given** a spool with no price whose colour is priced at 30.00 per kg, **When** a print uses 50 g, **Then** the line shows cost 1.50 and says the price came from the colour.
3. **Given** a spool and colour with no price whose filament is priced at 25.00 per kg, **When** a print uses 50 g, **Then** the line shows cost 1.25 and says the price came from the filament.
4. **Given** no price at any of the three levels, **When** a print ends, **Then** the line shows "no price" with no cost, and the total is marked partial.
5. **Given** a print that fails after using 40% of a line's estimate, **When** it ends, **Then** the line's cost covers only the grams actually deducted.
6. **Given** a line that was not deducted because no spool was bound, **When** the print ends, **Then** the line shows "not deducted" with no cost, and the total is marked partial.
7. **Given** that line, **When** the operator assigns a priced spool to it and the deduction completes, **Then** the line gains a cost using that spool's price.
8. **Given** a line that already has a recorded price, **When** catalog or spool prices change, or the line is deducted again, **Then** its recorded price and cost do not change.
9. **Given** a print started on the printer itself, **When** it ends, **Then** its lines are costed the same way as a dispatched print.
10. **Given** a spool priced at 0, **When** a print uses it, **Then** the line shows cost 0.00 from the spool and is not marked as having no price.
11. **Given** the assembly Build panel, **When** it shows a filament cost estimate, **Then** it still uses current catalog prices and is unaffected by recorded print costs.

---

### Edge Cases

- A print that used more than the spool had left: the spool's remaining weight stops at 0, and the history entry still shows the grams the print used, with 0 g remaining afterwards.
- A deduction run a second time for the same line (for example after assigning a spool): only the additional grams are recorded, never the same grams twice.
- One print that draws from the same spool through two lines: each deduction is its own history entry.
- A print entry whose print no longer exists, or a manual usage entry that names an unknown print: the entry stays visible without a link.
- A weight edit that races with a deduction on the same spool: the operator gets a conflict message, and neither the weight nor its history entry is saved.
- A multi-colour print where some lines are priced and some are not: the total adds the priced lines and is marked partial.
- Rounding: line costs and totals show two decimals. The total is computed from unrounded line costs and then rounded, so it can differ by 0.01 from adding the displayed line costs.
- Prints that ended before this feature shipped have no recorded price or cost. They show as not costed and are not back-filled.
- Spools with a price paid but registered at less than full weight: price per kg uses the spool's full weight, since the price paid covers the whole spool.
- Supplier and order number longer than 100 characters, or a correction reason longer than 200 characters, are refused with a clear message.

## Requirements *(mandatory)*

### Functional Requirements

**Spool history**

- **FR-001**: A spool's page MUST show that one spool directly by its identity, and its load time MUST NOT grow with the number of spools in the inventory. An unknown spool MUST show a clear not-found state.
- **FR-002**: The system MUST record a history entry for every change to a spool's remaining weight: each deduction by a print (dispatched or started on the printer), each manual usage entry, and each manual weight correction.
- **FR-003**: Each history entry MUST hold the time, the change in grams (a decrease for usage; a decrease or an increase for a correction), the remaining weight afterwards, the source, an optional reason, and for a print the print it came from.
- **FR-004**: The spool page MUST list history newest first, show the latest 50 entries, and state the total count when there are more.
- **FR-005**: An entry from a print MUST link to that print, and MUST still show without a link when the print no longer exists.
- **FR-006**: Saving a spool edit that changes the remaining weight MUST add a correction entry with the operator's optional reason (up to 200 characters). A save that does not change the weight MUST NOT add an entry.
- **FR-007**: A weight change and its history entry MUST be saved together or not at all. A conflicting concurrent change MUST be reported to the operator as a conflict.
- **FR-008**: History entries recorded before this feature MUST remain visible, with the remaining weight shown as unknown.
- **FR-009**: When a print reaches a final state, spool lists, spool pages, printer filament meters and the dashboard MUST show the updated remaining weights without a manual reload.
- **FR-010**: History entries MUST be append-only: operators cannot edit or delete them.

**Purchase info and adding several spools**

- **FR-011**: Adding spools MUST accept a quantity, a whole number from 1 to 50 (default 1), and MUST create that many independent spools in one step, all or none.
- **FR-012**: Each spool MAY carry a price paid (0 or more; zero is a real price and differs from unknown), a supplier (up to 100 characters), an order number (up to 100 characters), and a received date (a calendar date).
- **FR-013**: Price paid, supplier, order number and received date MUST be editable later in the existing single-Save spool edit, with the label and remaining weight.
- **FR-014**: Price paid MUST be a plain number with no currency symbol, and MUST be understood as the price of the whole spool. Price per kg derives from it divided by the spool's weight.
- **FR-015**: Changing a spool's price MUST NOT change the recorded cost of past prints, nor any filament or colour catalog price.
- **FR-016**: Every spool added MUST appear in the activity log like any other spool registration.
- **FR-017**: Adding spools MUST keep today's rules: a colour is required, weight is positive, remaining weight does not exceed spool weight, the label is optional, and filaments and vendors are not created from this page. With quantity above 1, the label, remaining weight and purchase values apply to every spool.

**Print filament cost**

- **FR-018**: When a print reaches a final state, the system MUST record, for each filament line deducted from a spool, the price per kg used and where it came from (spool, colour, or filament). This applies to dispatched prints and prints started on the printer.
- **FR-019**: The price MUST be chosen in this order: the spool's price paid divided by its weight (per kg); otherwise the colour's price per kg; otherwise the filament's price per kg. A price of zero counts as a price. If none exists, the line has no price.
- **FR-020**: A line's cost MUST be the grams deducted times the recorded price per kg, divided by 1000. A line with no price, or with nothing deducted, has no cost. A failed or cancelled print costs only what was deducted.
- **FR-021**: A recorded price MUST NOT change when catalog or spool prices change later, or when the same line is deducted again. A line with no recorded price MAY gain one when the operator assigns a priced spool and the deduction completes.
- **FR-022**: A print's total filament cost MUST be the sum of the lines that have a cost, and MUST be marked partial when any line has no cost, including lines not deducted.
- **FR-023**: The print page MUST show for each line the material and colour, the grams, the cost (or "no price" or "not deducted"), the price source, and a link to the spool used. It MUST also show the total and the partial marker. This applies to dispatched prints and prints started on the printer.
- **FR-024**: The assembly Build-panel filament estimate MUST keep using current catalog prices as a forecast. The documentation MUST state the difference between that forecast and the recorded cost.
- **FR-025**: Costs and totals MUST show two decimals with no currency symbol. Totals MUST be computed from unrounded line costs.
- **FR-026**: Prints that ended before this feature MUST show as not costed. No cost is back-filled.

### Key Entities *(include if feature involves data)*

- **Spool**: One physical roll. Existing attributes stay. New optional purchase info: price paid for the whole spool, supplier, order number, received date.
- **Spool history entry**: One change to a spool's remaining weight. Holds the spool, the time, the change in grams, the remaining weight afterwards (unknown for older entries), the source (print, manual usage, or weight correction), an optional reason, and for prints the print it came from. Append-only.
- **Print filament line**: One material/colour line of a print, with its estimated grams, grams deducted and spool. New: the price per kg recorded for it, the price source, and the cost derived from them.
- **Filament and colour price**: Existing optional price per kg on a filament and on each of its colours, used when the spool has no price.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can find which print last used a given spool, and how much it took, from the spool page alone in under 30 seconds, for a spool with up to 50 history entries.
- **SC-002**: 100% of weight changes to a spool (print deductions, manual usage, corrections) appear in its history. For a spool added after this feature, its starting remaining weight minus the net of its recorded changes equals its current remaining weight, except where a deduction was capped at 0 g.
- **SC-003**: After a print ends, remaining weights on spool lists, spool pages and the dashboard are up to date within 5 seconds with no manual reload.
- **SC-004**: Adding 10 identical spools takes one form submission instead of 10.
- **SC-005**: For every print that ends after release and has at least one priced, deducted line, the print page shows each line's cost and a total. Changing any catalog or spool price afterwards changes none of the displayed costs.
- **SC-006**: Every print line without a price or without a deduction is flagged: 100% of totals that omit such a line show the partial marker, and none counts it as zero.

## Assumptions

- This is a single-farm install; operators share one spool inventory and one price list.
- Price paid is for the whole spool, and the spool's registered weight is the net filament weight, so price per kg is price paid divided by that weight in kilograms.
- Amounts are plain numbers. The app has no currency setting, so no currency symbol appears anywhere.
- Prints that ended before release are not back-filled with costs or history; old history entries lack a remaining weight.
- The history keeps entries indefinitely, and the page shows the latest 50. Paging or filtering older entries can follow if needed.
- Existing behaviours are reused as they are: the Action required "Assign spool" flow, activity logging for every change, and the existing conflict handling for concurrent edits.
- Quantity is capped at 50 per submission, and supplier and order number at 100 characters each.
- The received date accepts any calendar date. The form proposes today.
- Real-time refresh follows the project's rule that live data updates without manual refresh (constitution principle III).
- Out of scope: low-stock thresholds, stock totals and tiles, finish reasons and restoring finished spools, enough-filament checks before dispatch, usage or cost reports and CSV export, RFID or QR spool identification, weight-only bulk stock, price history, and multi-line delivery entry.
