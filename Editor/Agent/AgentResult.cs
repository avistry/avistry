namespace Avistry.Textures
{
    /// <summary>
    /// The answer to one AI helper call. <see cref="data"/> is plain JSON-shaped data (Dictionary of string to object,
    /// List of object, string, bool, numbers, null) so the bridge can turn it into JSON without knowing the tool.
    /// </summary>
    internal class AgentResult
    {
        /// <summary>The error codes of the helper tools.</summary>
        internal static class Codes
        {
            internal const string AccessOff = "access_off";
            internal const string ReadOnly = "read_only";
            internal const string NoWindow = "no_window";
            internal const string NoTarget = "no_target";
            internal const string NotFound = "not_found";
            internal const string InvalidParam = "invalid_param";
            internal const string Busy = "busy";
            internal const string RequestOpen = "request_open";
            internal const string Denied = "denied";
            internal const string Expired = "expired";
            internal const string TooLarge = "too_large";
            internal const string Unsupported = "unsupported";
        }

        internal const string AccessOffHint = "Ask the person to open Help › Connect an AI helper… in Avistry.";
        internal const string ReadOnlyHint = "Ask the person to choose Look and edit in Help › Connect an AI helper… in Avistry.";

        public bool ok;
        public string code;       // set when ok is false
        public string action;
        public string message;    // plain words; may name assets, never a drive or home path
        public string hint;       // what to try next
        public string[] changed;  // what this call changed, in plain words
        public string undo;       // the Undo step name, when one was made
        public string toast;      // the message the window showed, if any
        public bool pending;      // waiting for the person; the server polls again
        public object data;
        internal bool partial;    // failed after it had already changed something: still logged, so its Undo step is not hidden

        internal static AgentResult Ok(string action, object data = null, string undo = null, string toast = null, params string[] changed)
            => new AgentResult { ok = true, action = action, data = data, undo = undo, toast = toast, changed = changed ?? new string[0] };

        internal static AgentResult Fail(string action, string code, string message, string hint = null)
            => new AgentResult { ok = false, action = action, code = code, message = message, hint = hint };

        internal static AgentResult Pending(string action, string message, object data = null)
            => new AgentResult { ok = true, pending = true, action = action, message = message, data = data };
    }
}
