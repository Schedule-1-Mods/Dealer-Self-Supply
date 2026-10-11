SHORT DESCRIPTION (unchanged - still correct for 1.18.6)
---------------------------
Stop running product to your dealers and collecting their cash by hand. Give each or all dealers a shelf to restock from and a shelf to drop their earnings in, and they handle the rest themselves. They walk there, through town and back, and carry on dealing afterwards.


FULL DESCRIPTION (same layout as the page: headings in [size=4][b]...[/b][/size])
----------------

[size=4][b]Description[/b][/size]
Stop running product to your dealers and collecting their cash by hand. Give each or all dealers a shelf to restock from and a shelf to drop their earnings in, and they handle the rest themselves. They walk there, through town and back, and carry on dealing afterwards.

[size=4][b]Installation instructions[/b][/size]
1. Install MelonLoader 0.7.x into Schedule I and start the game once so it sets itself up.
2. Put "Dealer Self-supply.dll" into the game's Mods folder.
3. Start the game and load your save.

[size=4][b]Main features[/b][/size]
Dealers restock themselves:
- Once a dealer has sold out, they walk to their product shelf and take everything on it they can carry: baggies, jars and bricks as they are, and loose product bagged up for them. No baggies needed on the shelf.
- They fill every inventory slot, then say "Inventory full, starting to deal."
- If the shelf is empty they say ""Dealer Products Shelf" is empty, I'll go back home." and walk back home.

Dealers drop off their cash:
- After a deal, on their way home, dealers stop at their EARNINGS shelf and leave all their cash there.
- Every cash drop also tops them up from their product shelf, so they head back out with a full inventory.
- If they have no cash they say "I have $0, I'll go back home."
- Shift-click the cash in the shelf to take the whole stack in one go.

You're the boss:
- When you send a dealer to their shelves, they go straight away, even in the middle of a deal. Their deals wait until they're done.

Three ways to set it up:
- Talk to a dealer (E): Product and Earnings blocks with PLACE and SHELF dropdowns, plus "Send to shelf now".
- Open any shelf, rack or locker: a panel shows who uses it (their faces side by side, with what each is carrying), a Yes/No for each dealer, and "Send them to their shelves now". If a dealer already uses another shelf, it asks in orange before moving them (Continue / Cancel).
- The Dealer Self-supply phone app: every dealer at a glance (faces, shelves, what they're doing, what they're carrying, and the cash on their earnings shelf), with "Put cash away", "Collect product" and "Both" buttons. Cash a dealer is holding shows in green, $0 in red.

Dealers keep you posted:
- Pop-ups with the dealer's face tell you what they're doing. "On my way to "Dealer Earnings Shelf" with cash." shows in green, so you can see who's actually going. Problems like "I have $0, I'll go back home." or ""Dealer Products Shelf" is empty, I'll go back home." show in red.
- A Dealer Self-supply icon with a red dot appears to the right of the Messages icon until you've checked the app. It steps aside while the game's own pop-ups are showing.

Shelves you can tell apart:
- Every dealer shelf gets a small white name board on both sides, sitting flat on the front and back, with the faces of the dealers who use it next to the name. It fits safes, lockers and racks of any size.
- Rename a shelf from its panel. The new name is saved with your game.

Other features:
- Several dealers can share a shelf.
- The shelf panel opens at the top right of the screen. All panels can be dragged anywhere on screen, and each player's layout is saved on their own PC.
- Works with the Greenhouse Island mod: dealers walk down the walkways onto the island instead of being teleported.

[size=4][b]Settings[/b][/size]
In UserData/DealerSelfSupply.cfg (created the first time the game loads the mod):
- RestockWhenSoldOut (true): only restock once every slot is empty. Set it to false to use RestockWhenEmptyPercent instead.
- RestockWhenEmptyPercent (40)
- TravelSpeedMultiplier (1.5): dealers move a bit faster on shelf runs.
- MaxTravelSeconds (300): a dealer who can't reach a shelf by then is moved to it.
- CheckIntervalSeconds (20)
- EmptyShelfCooldownSeconds (120)
- ShowNotifications (true)
- NotificationSeconds (10)

[size=4][b]Requirements[/b][/size]
- Schedule I, IL2CPP version (the normal Steam branch). Built and tested on 0.4.6f13. The 0.4.7 open beta isn't fully tested yet - please report any problems there.
- MelonLoader 0.7.x (made with 0.7.3).
- Not for the Mono ("alternate") branch.
- Multiplayer: the host runs it. Only the host sees the panels and the app.
- Dealers need a shelf or locker you've placed, and must be hired.

[size=4][b]Troubleshooting[/b][/size]
- Check the mod loaded: MelonLoader/Latest.log should have a line like "[Dealer_Self-supply] Dealer Self-supply 1.18.6 loaded (game 0.4.6f13)". If it says "Failed to load Melon" instead, you're probably on the Mono "alternate" branch or an old MelonLoader.
- Settings are in UserData/DealerSelfSupply.cfg (not MelonPreferences.cfg).
- Dealers take all product (baggies, jars, bricks, and loose product, which they bag). They don't take ingredients or other items.
- If a dealer won't go, their message says why (for example "Busy right now (...)"). Please post that, plus the log lines with "Dealer_Self-supply", in the comments.
- If the game can't find a path to the shelf (for example a shelf inside a building dealers can't walk into), the dealer is moved there after 8 seconds. The log says "can't find a way to ...".
- A dealer who gets stuck on the way is nudged after 20 seconds, then moved to the shelf. The log line "hasn't moved in 20s" says where; please post it if it keeps happening.
- Mods that pause or freeze far-away NPCs (like Siesta - NPC LOD) can stop dealers setting off. Try without them if dealers never move.
- To uninstall, delete "Dealer Self-supply.dll". Your shelf picks are kept in UserData/DealerSelfSupply_Shelves.json, which you can delete too.


CHANGELOG 1.18.6
----------------
- Shelf panel: the dealers using a shelf are shown side by side (face, name, items and cash on them); all names are white.
- Assigning a dealer who already uses another shelf now asks first, in orange: Continue or Cancel.
- Name boards show the faces of the dealers who use the shelf.
- Dealers the game can't find a path for no longer stand still: they're un-paused and retried, then moved to the shelf after 8 seconds (for the "dealers register their shelves but don't move" report).
- Better performance: the mod looks for shelves far less often (the phone app used to do it several times a frame).
- Teleporting a stuck dealer now also works on the 0.4.7 beta.
- Tidier install: only the DLL goes in Mods. Shelf picks moved to UserData/DealerSelfSupply_Shelves.json and are moved there automatically; your settings carry over.

CHANGELOG 1.17.7
----------------
- Dealers take everything on their product shelf: baggies, jars and bricks as they are, and loose product bagged up (no baggies needed on the shelf). They fill every slot, then say "Inventory full, starting to deal."
- Sending a dealer (from the app, a shelf or by talking to them) now comes first: they go straight away, even mid-deal, and deals wait until they're done.
- "Put cash away" goes straight to the earnings shelf; every cash drop also tops them up from their product shelf.
- New messages: "I have $0, I'll go back home." and ""X" is empty, I'll go back home." Dealers walk home afterwards.
- "On my way..." messages are green, problems are red.
- Phone app: dealer names in white; cash on them green, $0 red.
- Name boards are smaller and sit flat on the shelf (no gap on safes and lockers).
- The shelf panel opens at the top right.
- The app icon sits to the right of the Messages icon with a gap, and no longer covers the game's pop-ups.
- Dealers who get stuck on the way (seen on the Greenhouse Island walkways) are nudged after 20 seconds, then moved to the shelf.
