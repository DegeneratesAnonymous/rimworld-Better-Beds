using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace SmarterBeds
{
    /// <summary>
    /// Adds an "Assign rooms" button to the top right of the Assign tab. The tab is one shared table
    /// window (MainTabWindow_PawnTable), so the patch checks it is the Assign one.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_PawnTable), nameof(MainTabWindow_PawnTable.DoWindowContents))]
    public static class AssignTab_RoomsButton_Patch
    {
        private const float ButtonWidth = 140f;
        private const float ButtonHeight = 28f;

        static void Postfix(MainTabWindow_PawnTable __instance, Rect rect)
        {
            if (!(__instance is MainTabWindow_Assign) || !SmarterBedsMod.Settings.enabled) return;

            // Top right, above the Carry columns and clear of the scroll bar.
            var button = new Rect(rect.xMax - ButtonWidth - 24f, rect.y + 4f, ButtonWidth, ButtonHeight);
            TooltipHandler.TipRegion(button, "SmarterBeds_AssignRooms_Tip".Translate());
            if (!Widgets.ButtonText(button, "SmarterBeds_AssignRooms".Translate())) return;

            var open = Find.WindowStack.WindowOfType<Window_RoomList>();
            if (open != null) open.Close();
            else Find.WindowStack.Add(new Window_RoomList());
        }
    }

    /// <summary>
    /// Every room that has beds in it, with how many are taken, how good the room is, how big, and who
    /// lives there. Hover a row to point at its beds in the world, double-click to jump the camera
    /// there, and use Assign to choose owners. Assigning goes through the game's own "Set owner"
    /// window, so a bed you assign here is locked against negotiation like any other.
    /// </summary>
    public class Window_RoomList : Window
    {
        private enum SortBy { Beds, Score, Size, Owners }

        private class RoomRow
        {
            public Room room; // null for a bed that is not in an enclosed room
            public List<Building_Bed> beds = new List<Building_Bed>();
            public int owners;
            public int capacity;
            public float score;
            public int cells;
            public string size;
            public string ownerNames;
        }

        private const float RowHeight = 30f;
        private const float AssignWidth = 90f;
        private const float BedsWidth = 70f;
        private const float ScoreWidth = 100f;
        private const float SizeWidth = 80f;

        private List<RoomRow> rows = new List<RoomRow>();
        private Vector2 scroll;
        private SortBy sortBy = SortBy.Score;
        private bool descending = true;
        private float lastBuilt = -10f;

        public override Vector2 InitialSize => new Vector2(720f, 560f);

        public Window_RoomList()
        {
            doCloseX = true;
            draggable = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
            onlyOneOfTypeAllowed = true;
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            if (Time.realtimeSinceStartup - lastBuilt > 0.5f) Rebuild();
        }

        private void Rebuild()
        {
            lastBuilt = Time.realtimeSinceStartup;
            rows = BuildRows(Find.CurrentMap);
            Sort();
        }

        private static List<RoomRow> BuildRows(Map map)
        {
            var result = new List<RoomRow>();
            if (map == null) return result;

            var beds = map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>()
                .Where(b => b.Spawned && b.ForColonists && !b.Medical)
                .ToList();

            foreach (var group in beds.GroupBy(b => { var r = b.GetRoom(); return r == null || r.PsychologicallyOutdoors ? null : r; }))
            {
                if (group.Key == null)
                {
                    foreach (var bed in group) result.Add(MakeRow(null, new List<Building_Bed> { bed }));
                }
                else result.Add(MakeRow(group.Key, group.ToList()));
            }
            return result;
        }

        private static RoomRow MakeRow(Room room, List<Building_Bed> beds)
        {
            var row = new RoomRow { room = room, beds = beds };
            var names = new List<string>();
            foreach (var bed in beds)
            {
                row.capacity += bed.SleepingSlotsCount;
                row.owners += bed.OwnersForReading.Count;
                foreach (var owner in bed.OwnersForReading) names.Add(owner.LabelShort);
            }
            row.ownerNames = string.Join(", ", names);

            if (room != null)
            {
                row.score = room.GetStat(RoomStatDefOf.Impressiveness);
                row.cells = room.CellCount;
                var extents = room.ExtentsClose;
                row.size = extents.Width + "x" + extents.Height;
            }
            else
            {
                row.size = "-";
            }
            return row;
        }

        private void Sort()
        {
            System.Comparison<RoomRow> compare;
            switch (sortBy)
            {
                case SortBy.Beds: compare = (a, b) => (a.capacity - a.owners).CompareTo(b.capacity - b.owners); break;
                case SortBy.Size: compare = (a, b) => a.cells.CompareTo(b.cells); break;
                case SortBy.Owners: compare = (a, b) => string.Compare(a.ownerNames, b.ownerNames, System.StringComparison.OrdinalIgnoreCase); break;
                default: compare = (a, b) => a.score.CompareTo(b.score); break;
            }
            rows.Sort((a, b) => descending ? compare(b, a) : compare(a, b));
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "SmarterBeds_RoomsTitle".Translate());
            Text.Font = GameFont.Small;

            float top = inRect.y + 36f;
            float listWidth = inRect.width - 16f;
            float ownersWidth = listWidth - BedsWidth - ScoreWidth - SizeWidth - AssignWidth - 12f;

            // Header: each label sorts by that column.
            var header = new Rect(inRect.x, top, listWidth, 26f);
            float x = header.x + 6f;
            x = HeaderCell(x, header.y, BedsWidth, "SmarterBeds_RoomsBeds".Translate(), SortBy.Beds, "SmarterBeds_RoomsBeds_Tip".Translate());
            x = HeaderCell(x, header.y, ScoreWidth, "SmarterBeds_RoomsScore".Translate(), SortBy.Score, "SmarterBeds_RoomsScore_Tip".Translate());
            x = HeaderCell(x, header.y, SizeWidth, "SmarterBeds_RoomsSize".Translate(), SortBy.Size, "SmarterBeds_RoomsSize_Tip".Translate());
            HeaderCell(x, header.y, ownersWidth, "SmarterBeds_RoomsOwners".Translate(), SortBy.Owners, null);
            Widgets.DrawLineHorizontal(inRect.x, header.yMax, listWidth);

            var outRect = new Rect(inRect.x, header.yMax + 2f, inRect.width, inRect.yMax - header.yMax - 28f);
            var viewRect = new Rect(0f, 0f, listWidth, rows.Count * RowHeight);

            if (rows.Count == 0)
            {
                Widgets.NoneLabel(outRect.y + 8f, outRect.width, "SmarterBeds_RoomsNone".Translate());
            }

            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            for (int i = 0; i < rows.Count; i++)
            {
                DoRow(new Rect(0f, i * RowHeight, listWidth, RowHeight), rows[i], i, ownersWidth);
            }
            Widgets.EndScrollView();

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(inRect.x, inRect.yMax - 22f, inRect.width, 22f), "SmarterBeds_RoomsHint".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private float HeaderCell(float x, float y, float width, string label, SortBy column, string tip)
        {
            var rect = new Rect(x, y, width, 26f);
            string arrow = sortBy == column ? (descending ? " v" : " ^") : "";
            if (Widgets.ButtonText(rect, label + arrow, drawBackground: false))
            {
                if (sortBy == column) descending = !descending;
                else { sortBy = column; descending = column == SortBy.Score; }
                Sort();
            }
            if (tip != null) TooltipHandler.TipRegion(rect, tip);
            return x + width;
        }

        private void DoRow(Rect row, RoomRow data, int index, float ownersWidth)
        {
            if (index % 2 == 1) Widgets.DrawLightHighlight(row);
            Widgets.DrawHighlightIfMouseover(row);

            var assign = new Rect(row.xMax - AssignWidth - 4f, row.y + 2f, AssignWidth, RowHeight - 4f);
            bool overRow = Mouse.IsOver(row) && !Mouse.IsOver(assign);

            // Point at the beds while hovering.
            if (overRow)
            {
                foreach (var bed in data.beds) TargetHighlighter.Highlight(new GlobalTargetInfo(bed), arrow: true, colonistBar: false, circleOverlay: true);
                TooltipHandler.TipRegion(row, BedTooltip(data));

                // Double-click: jump the camera to the room.
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && Event.current.clickCount == 2)
                {
                    CameraJumper.TryJump(new GlobalTargetInfo(data.beds[0]));
                    Event.current.Use();
                }
            }

            float x = row.x + 6f;
            string beds = data.owners + "/" + data.capacity;
            Widgets.Label(new Rect(x, row.y + 4f, BedsWidth, RowHeight), beds);
            x += BedsWidth;
            Widgets.Label(new Rect(x, row.y + 4f, ScoreWidth, RowHeight), data.room != null ? data.score.ToString("F0") : "-");
            x += ScoreWidth;
            Widgets.Label(new Rect(x, row.y + 4f, SizeWidth, RowHeight), data.size);
            x += SizeWidth;

            string names = data.ownerNames.NullOrEmpty() ? "SmarterBeds_RoomsNoOwners".Translate().ToString() : data.ownerNames;
            Text.WordWrap = false;
            Widgets.Label(new Rect(x, row.y + 4f, ownersWidth, RowHeight), names.Truncate(ownersWidth));
            Text.WordWrap = true;

            if (Widgets.ButtonText(assign, "SmarterBeds_RoomsAssign".Translate())) OpenAssign(data);
        }

        private static string BedTooltip(RoomRow data)
        {
            var lines = new List<string>();
            foreach (var bed in data.beds)
            {
                string owners = bed.OwnersForReading.Count == 0
                    ? "SmarterBeds_RoomsNoOwners".Translate().ToString()
                    : string.Join(", ", bed.OwnersForReading.Select(p => p.LabelShort));
                lines.Add(bed.LabelCap + " (" + bed.OwnersForReading.Count + "/" + bed.SleepingSlotsCount + "): " + owners);
            }
            if (data.room != null) lines.Add("SmarterBeds_RoomsCells".Translate(data.cells));
            return string.Join("\n", lines);
        }

        /// <summary>One bed: straight to the game's Set owner window. Several: pick the bed first.</summary>
        private static void OpenAssign(RoomRow data)
        {
            if (data.beds.Count == 1)
            {
                OpenOwnerWindow(data.beds[0]);
                return;
            }

            var options = new List<FloatMenuOption>();
            foreach (var bed in data.beds)
            {
                var captured = bed;
                string owners = bed.OwnersForReading.Count == 0
                    ? "SmarterBeds_RoomsNoOwners".Translate().ToString()
                    : string.Join(", ", bed.OwnersForReading.Select(p => p.LabelShort));
                string label = bed.LabelCap + " at (" + bed.Position.x + ", " + bed.Position.z + ") - " +
                               bed.OwnersForReading.Count + "/" + bed.SleepingSlotsCount + ": " + owners;
                options.Add(new FloatMenuOption(label, () => OpenOwnerWindow(captured)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void OpenOwnerWindow(Building_Bed bed)
        {
            var comp = bed.CompAssignableToPawn;
            if (comp != null) Find.WindowStack.Add(new Dialog_AssignBuildingOwner(comp));
        }
    }
}
