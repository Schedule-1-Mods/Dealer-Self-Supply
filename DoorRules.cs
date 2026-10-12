namespace DealerSelfSupply
{
    // When a dealer should go through a building's door on a trip. Pure rules (no game types) so they
    // can be tested away from the game (Tools/selftest).
    //
    // The game says a shelf is "reachable" only if the walkable floor comes within about 1.6 m of
    // where the dealer should stand. A shelf whose standing spot is further than that from the floor
    // is "unreachable" even from inside its own room. The first version of the door code asked
    // "reachable?" again after stepping in, got "no", walked back out - and was sent in again by the
    // same question outside: in and out of the door twice a second, for ever.
    //
    // So: the place a dealer came in FOR is never a reason to leave (they walk as close as the floor
    // allows and step up to the shelf), a stop is only tried through a door once, and a trip crosses a
    // doorway at most MaxCrossings times whatever else happens.
    internal static class DoorRules
    {
        public const int MaxCrossings = 4;

        // Inside a building, heading for a place that is not the one they came in for, and the
        // game can't walk them there from in here: back out through the door first.
        public static bool ShouldExit(bool inside, bool isThePlaceTheyEnteredFor, bool reachable, int crossings) =>
            inside && !isThePlaceTheyEnteredFor && !reachable && crossings < MaxCrossings;

        // Outside, the place can't be walked to, and it hasn't been tried through a door yet: try the door.
        public static bool ShouldEnter(bool inside, bool reachable, bool alreadyTried, int crossings) =>
            !inside && !reachable && !alreadyTried && crossings < MaxCrossings;
    }
}
