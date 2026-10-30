using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace WilsBetterBeds
{
    /// <summary>
    /// The notification in the top left of the screen, but short-lived.
    ///
    /// The game's message popup lives for a fixed 13 seconds and fades over the last 0.6 (both are
    /// constants, not settings), so there is no duration to ask for. Instead the message is created
    /// already "aged": started that many seconds in the past, so it has exactly the time we want
    /// left, then fades out the way every other message does. If a game update renames the private
    /// field we use, the message simply shows for the normal time.
    /// </summary>
    public static class BriefMessage
    {
        private const float DefaultLifespan = 13f;
        private const float FadeDuration = 0.6f;

        private static readonly FieldInfo StartingTime = AccessTools.Field(typeof(Message), "startingTime");

        /// <param name="seconds">How long it stays fully visible before the fade.</param>
        public static void Show(string text, LookTargets targets, float seconds)
        {
            var message = new Message(text.CapitalizeFirst(), MessageTypeDefOf.NeutralEvent, targets);

            if (StartingTime != null && StartingTime.FieldType == typeof(float))
            {
                float aged = DefaultLifespan - FadeDuration - Mathf.Max(0f, seconds);
                StartingTime.SetValue(message, RealTime.LastRealTime - Mathf.Max(0f, aged));
            }

            // Kept in the message history so the roll totals can be looked up afterwards.
            Messages.Message(message, true);
        }
    }
}
