using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace ActionParse
{
    class ActionEntry
    {
        public int Id;
        public string Name;
        public uint Potency;
        public uint ComboPotency;
        public uint FlankPotency;
        public uint FrontPotency;
        public uint RearPotency;
        public uint CurePotency;
        public uint RestorePercentage;

        public bool IsEmpty => Potency == 0 &&
                               ComboPotency == 0 &&
                               FlankPotency == 0 &&
                               FrontPotency == 0 &&
                               RearPotency == 0 &&
                               CurePotency == 0 &&
                               RestorePercentage == 0;

        public override string ToString()
        {
            return $"  // {Name}\n  {{ {Id}, {{ {Potency}, {ComboPotency}, {FlankPotency}, {FrontPotency}, {RearPotency}, {CurePotency}, 0 }} }},";
        }
    }

    class Program
    {
        private const string GameDirectory = @"D:\SquareEnix\FINAL FANTASY XIV - A Realm Reborn";
        private const string OutputFileName = "ActionLutData.cpp";

        private static readonly Regex potencyRegex = new Regex(@"with a potency of ([\d,]+)", RegexOptions.Compiled);
        private static readonly Regex rearPotencyRegex = new Regex(@"([\d,]+) when executed from a target's rear", RegexOptions.Compiled);
        private static readonly Regex flankPotencyRegex = new Regex(@"([\d,]+) when executed from a target's flank", RegexOptions.Compiled);
        private static readonly Regex frontPotencyRegex = new Regex(@"([\d,]+) when executed in front of target", RegexOptions.Compiled);
        private static readonly Regex comboPotencyRegex = new Regex(@"Combo Potency: ([\d,]+)", RegexOptions.Compiled);
        private static readonly Regex curePotencyRegex = new Regex(@"Cure Potency: ([\d,]+)", RegexOptions.Compiled);
        private static readonly Regex restoresRegex = new Regex(@"Restores (\d+%)", RegexOptions.Compiled);

        static void Main(string[] args)
        {
            var realm = new SaintCoinach.ARealmReversed(GameDirectory, SaintCoinach.Ex.Language.English);
            //if (!realm.IsCurrentVersion)
            //{
            //    const bool IncludeDataChanges = true;
            //    var updateReport = realm.Update(IncludeDataChanges);
            //}

            var entries = new List<ActionEntry>();
            var actionElements = new List<SaintCoinach.Text.INode>();

            var actSheet = realm.GameData.GetSheet<SaintCoinach.Xiv.Action>();
            foreach (var act in actSheet)
            {
                if (act.Name == null || act.Name.IsEmpty)
                    continue;

                if (act.ClassJob == null || act.ClassJob.Name == null || act.ClassJob.Name.IsEmpty)
                    continue;

                if (act.AsBoolean("IsPvP"))
                    continue;

                // could do Key == 32 or 33 but this is clearer
                if (act.ClassJob.ClassJobCategory.Name == "Disciple of the Land" || act.ClassJob.ClassJobCategory.Name == "Disciple of the Hand")
                    continue;

                if (act.ActionCategory.Name != "Ability" &&
                    act.ActionCategory.Name != "Auto-attack" &&
                    act.ActionCategory.Name != "Spell" &&
                    act.ActionCategory.Name != "Weaponskill" &&
                    act.ActionCategory.Name != "Limit Break")
                    continue;

                Console.WriteLine($"{act.Key} - {act.Name}");

                var entry = new ActionEntry
                {
                    Id = act.Key,
                    Name = act.Name
                };

                actionElements.Clear();
                foreach (var node in act.ActionTransient.Description.Children)
                {
                    FilterNode(node, actionElements);
                }
                PopulateEntry(entry, actionElements);

                // Console.WriteLine(entry);

                // this doesn't match the c++ behavior, but it was mentioned that empty skills are pointless...
                if (!entry.IsEmpty)
                {
                    entries.Add(entry);
                }
            }

            Console.WriteLine($"Found {entries.Count()} player actions");

            OutputCpp(entries, OutputFileName);

            Console.ReadLine();
        }

        static void FilterNode(SaintCoinach.Text.INode node, List<SaintCoinach.Text.INode> nodes)
        {
            // Discard these for now since they're just noise for what we currently parse
            if (node.Tag == SaintCoinach.Text.TagType.UIForeground ||
                node.Tag == SaintCoinach.Text.TagType.UIGlow)
                return;

            // for conditionals, just take the false branch as it should be right initially,
            // and nothing in sapphire is setup to handle ability evolution
            if (node.Tag == SaintCoinach.Text.TagType.If)
            {
                FilterNode(((SaintCoinach.Text.Nodes.IfElement)node).FalseValue, nodes);
                return;
            }

            if (node.Tag == SaintCoinach.Text.TagType.IfEquals)
            {
                FilterNode(((SaintCoinach.Text.Nodes.IfEqualsElement)node).FalseValue, nodes);
                return;
            }

            nodes.Add(node);
        }

        static void PopulateEntry(ActionEntry entry, List<SaintCoinach.Text.INode> actionElements)
        {
            var dataString = new SaintCoinach.Text.XivString(actionElements.ToArray()).ToString();
            PopulateElement(dataString, potencyRegex, out entry.Potency);
            PopulateElement(dataString, rearPotencyRegex, out entry.RearPotency);
            PopulateElement(dataString, flankPotencyRegex, out entry.FlankPotency);
            PopulateElement(dataString, frontPotencyRegex, out entry.FrontPotency);
            PopulateElement(dataString, comboPotencyRegex, out entry.ComboPotency);
            PopulateElement(dataString, curePotencyRegex, out entry.CurePotency);
            PopulateElement(dataString, restoresRegex, out entry.RestorePercentage);
        }

        static void PopulateElement(string actionElementString, Regex regex, out uint element)
        {
            var match = regex.Match(actionElementString);
            if (match.Success)
            {
                uint.TryParse(match.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out element);
            }
            else
            {
                element = 0;
            }
        }

        static void OutputCpp(List<ActionEntry> actionEntries, string outputFile)
        {
            var template = String.Empty;

            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream("ActionLutData.cpp.tmpl"))
            using (var reader = new StreamReader(stream))
            {
                template = reader.ReadToEnd();
            }
            template = template.Replace("%INSERT_GARBAGE%", string.Join("\n", actionEntries.Select(entry => entry.ToString())));

            File.WriteAllText(outputFile, template);
        }
    }
}
