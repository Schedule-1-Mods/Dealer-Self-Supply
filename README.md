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
- **Sold out** (all their slots empty): they walk to their product shelf and take its product until they're full. Baggies, jars and bricks go as they are; loose product gets bagged up on the way (no baggies needed on the shelf), because dealers can only sell packaged product. Once every slot is filled they say **"Inventory full, starting to deal."** (or, if the shelf ran out first, that they took everything on it).
- **Heading home after dealing**: deals happen on the game's own timing. Once a dealer finishes a deal and has no other deal lined up, they stop at EARNINGS on the way home and drop off all their cash. It goes onto the cash stack already there, or into an empty slot. Every cash drop also tops them up from their product shelf, so they head back out with a full inventory.
- If they've also sold out, they do both in one trip: EARNINGS first, then product.

Afterwards they go back to their usual routine. **Send them to their shelves now** on the shelf panel sends them right away to do whatever they can.

**When you send them** (shelf panel, dealer chat or phone app), your orders come first: they go straight away, even mid-deal. The deal waits until they're done. With no cash to drop off they say "I have $0, I'll go back home." On their own, a dealer won't set off while they're on a deal, knocked out or busy with something else. If a new deal comes up mid-trip, they go to the deal and finish the trip afterwards. If they can't walk all the way to a shelf, they walk as close as they can and step the rest. If a trip takes too long, they're warped there.

In multiplayer the host runs everything (dealers only walk and move items on the host's game), so only the host gets the talk menu, the shelf panel and the app's buttons. A player who joins a game with the mod installed still gets the Dealer Self-supply app: a read-only overview of the dealers (face, items on them, cash, whether they're on a deal). Shelf assignments live on the host's PC, so a joiner can't see or change them - they ask the host.


## Phone app
A **Dealer Self-supply** app sits on the phone's home screen. Tap it for a table of every dealer: their face, product shelf, earnings shelf (with the cash on it), what they're doing, and what they're carrying. **Put cash away**, **Collect product** and **Both** send everyone with a shelf for that job. Anyone on a deal goes once it's done. The app closes when you put the phone away. The icon is `Assets/app_icon.png`.

## Greenhouse Island
With the Greenhouse Island mod installed, dealers walk to shelves on the island: down the walkway and into the dome, then back the same way. The island's walkable area doesn't join up with the game's, so they switch over at the foot of each walkway, where the two overlap. There's no visible jump.

## Settings (`UserData/DealerSelfSupply.cfg`)
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
| RestockAtNight | true | Dealers who are in bed (inside their home at night) also go for product when sold out; false lets them sleep |
| LogSlowFrames | false | Log a line whenever one of the mod's jobs takes over 8 ms (for tracking down stutters) |

Values outside the sensible range are pulled back into it. If the settings file ever gets damaged (for example after switching between mod versions), the mod repairs it when the game starts and keeps the old copy as `DealerSelfSupply.cfg.bak`.

Assignments are saved in `UserData/DealerSelfSupply_Shelves.json`. Files from 1.0.0 still load. The previous copy is kept as `DealerSelfSupply_Shelves.json.bak`.

## Game versions
IL2CPP builds of Schedule I: **0.4.6f13** and **0.4.7**. The game's NPC movement code changed in 0.4.7, so the mod looks the affected calls up by name when it starts and says which form it found in `MelonLoader/Latest.log` (the line starting `[Compat]`). A call the game doesn't have is replaced by Unity's own, or that one feature is skipped (for example the travel speed boost) - the mod itself keeps working. The Mono ("alternate") branch isn't supported.
