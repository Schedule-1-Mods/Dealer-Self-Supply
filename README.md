# Dealer Self-supply

Dealers look after themselves. Each dealer gets a **supply shelf** to grab product from and an **EARNINGS** shelf to drop their cash in, so you don't have to run around to them.

## Setting it up
**From the dealer:** look at a hired dealer and press **E**. While you're talking, two blocks show in the top-left corner: **Product** and **Earnings**. Each block shows:
- whether they're sold out, or how much cash they're holding
- two dropdowns

To choose a shelf:
1. **PLACE** lists the properties where you've placed shelves or lockers, with how many each has. Picking a place opens the shelf dropdown.
2. **SHELF** lists that place's shelves and lockers by name, marking any that are empty. The one the dealer uses is shown in green. Click one to choose it.

**Stop using a shelf** clears the choice. **Send to <shelf> now** sends the dealer to that shelf as soon as you finish talking. When you set an earnings shelf, the dealer takes the cash they're holding there straight away too.

Shelves are listed by the names you give them in game (e.g. PRODUCTS, EARNINGS) - the mod doesn't rename anything. One shelf can't be the same dealer's product and earnings shelf.

**From the shelf:** open any shelf, rack or other storage you've placed. A **Dealer Shelves** panel on the left shows whose shelf it is: a table of every dealer assigned to it, with their face, name, whether they take product or drop earnings there, and how much product and cash they have on them right now. Any cash on the shelf is shown underneath. Below that, each dealer has a **Product** toggle and an **Earnings** toggle for this shelf.

Each dealer has one product shelf and one EARNINGS shelf. Several dealers can share a shelf for either job. In the SHELF list, shelves other dealers take product from are marked with their names, e.g. `PRODUCTS (Benji, Wei)`.

**Collecting the cash:** open your EARNINGS shelf and **Shift-click** the cash. The whole stack goes into your cash in one go (the game on its own only moves $1,000 per click).

## What dealers do
- **Sold out** (all their slots empty): they walk to their product shelf and take packaged product (baggies, jars, bricks) until they're full.
- **Heading home after dealing**: deals happen on the game's own timing. Once a dealer finishes a deal and has no other deal lined up, they stop at EARNINGS on the way home and drop off all their cash. It goes onto the cash stack already there, or into an empty slot.
- If they've also sold out, they do both in one trip: EARNINGS first, then product.

Afterwards they go back to their usual routine. **Send them to their shelves now** on the shelf panel sends them right away to do whatever they can.

A dealer won't set off while they're on a deal, knocked out or busy with something else. If a new deal comes up mid-trip, they go to the deal and finish the trip afterwards. If they can't walk all the way to a shelf, they walk as close as they can and step the rest. If a trip takes too long, they're warped there.

In multiplayer only the host runs this, and only the host sees the options.


## Phone app
A **Dealer Self-supply** app sits on the phone's home screen. Tap it for a table of every dealer: their face, product shelf, earnings shelf (with the cash on it), what they're doing, and what they're carrying. **Put cash away**, **Collect product** and **Both** send everyone with a shelf for that job. Anyone on a deal goes once it's done. The app closes when you put the phone away. The icon is `Assets/app_icon.png`.

## Greenhouse Island
With the Greenhouse Island mod installed, dealers walk to shelves on the island: down the walkway and into the dome, then back the same way. The island's walkable area doesn't join up with the game's, so they switch over at the foot of each walkway, where the two overlap. There's no visible jump.

## Settings (`UserData/MelonPreferences.cfg`, `[DealerSupplyRun]`)
| Setting | Default | |
|---|---|---|
| CheckIntervalSeconds | 20 | How often dealers check whether they've sold out |
| RestockWhenSoldOut | true | Only go for product once every slot has sold out |
| RestockWhenEmptyPercent | 40 | With RestockWhenSoldOut off: go for product once this % of slots is empty |
| TravelSpeedMultiplier | 1.5 | Speed boost while on a trip |
| MaxTravelSeconds | 300 | Warp to the shelf if still walking after this |
| EmptyShelfCooldownSeconds | 120 | Wait this long after finding nothing to do |
| ShowNotifications | true | Show a notification when a dealer restocks or drops off cash |
| NotificationSeconds | 10 | How long those notifications stay on screen |

Assignments are saved in `Mods/DealerSupplyRun_Shelves.json`. Files from 1.0.0 still load.
