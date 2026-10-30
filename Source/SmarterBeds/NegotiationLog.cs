using RimWorld;
using Verse;

namespace SmarterBeds
{
    /// <summary>
    /// Writes negotiation lines to the game's play log as ordinary interactions. That is all
    /// Interaction Bubbles needs (it draws a bubble for every interaction added to the log), so
    /// with the mod installed the conversation appears over the speaker's head, and without it
    /// the lines still show up in the pawn's social log.
    ///
    /// The speaker is the "initiator" of the entry: the asker for the request, the defender for
    /// the answer, so each of them gets their own bubble.
    /// </summary>
    public static class NegotiationLog
    {
        public static void Say(string interactionDefName, Pawn speaker, Pawn listener)
        {
            if (!SmarterBedsMod.Settings.showInPlayLog || speaker == null || listener == null) return;
            var def = DefDatabase<InteractionDef>.GetNamedSilentFail(interactionDefName);
            if (def == null) return;
            Find.PlayLog.Add(new PlayLogEntry_Interaction(def, speaker, listener, null));
        }
    }
}
