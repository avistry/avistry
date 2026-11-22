using System.Collections.Generic;

namespace Avistry.Textures
{
    partial class AvatarTextureWindow
    {
        const string LearnNeedsSandbox = "Turn the skill sandbox on first: AutoLearnSandbox(true).";

        /// <summary>Keeps skill progress in memory so a test never touches the real progress. Returns "sandbox=on" or "sandbox=off".</summary>
        internal string AutoLearnSandbox(bool on)
        {
            Learn.Sandbox = on;
            return "sandbox=" + (on ? "on" : "off");
        }

        /// <summary>"New", "Read" or "Done" for a concept, or "unknown concept".</summary>
        internal string AutoLearnState(string conceptId)
        {
            if (!Glossary.Has(conceptId)) return "unknown concept";
            return Learn.StateOf(conceptId).ToString();
        }

        /// <summary>
        /// Fires a learn event like the tool does, in the sandbox only. Returns "done=" and the comma-separated concepts
        /// that became Done (empty when none), or "unknown event", or why it did not run.
        /// </summary>
        internal string AutoLearnDid(string learnEvent)
        {
            if (!Learn.Sandbox) return LearnNeedsSandbox;
            if (!LearnEvents.Has(learnEvent)) return "unknown event";
            var before = new List<string>();
            var ids = Glossary.ConceptsDoneBy(learnEvent);
            if (ids != null) foreach (var id in ids) if (!Learn.IsDone(id)) before.Add(id);
            Learn.Did(learnEvent);
            before.RemoveAll(id => !Learn.IsDone(id));
            return "done=" + string.Join(",", before);
        }

        /// <summary>The "I've got it" button, in the sandbox only. Returns null, or why it did not run.</summary>
        internal string AutoLearnGotIt(string conceptId)
        {
            if (!Learn.Sandbox) return LearnNeedsSandbox;
            var e = Glossary.Get(conceptId);
            if (e == null) return "unknown concept";
            if (!e.gotIt) return "This concept has no \"I've got it\" button.";
            Learn.GotIt(conceptId);
            return null;
        }
    }
}
