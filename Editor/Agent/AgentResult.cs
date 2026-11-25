namespace Avistry.Textures
{
    internal class AgentResult
    {
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
        public string code;
        public string action;
        public string message;
        public string hint;
        public string[] changed;
        public string undo;
        public string toast;
        public bool pending;
        public object data;
        internal bool partial;

        internal static AgentResult Ok(string action, object data = null, string undo = null, string toast = null, params string[] changed)
            => new AgentResult { ok = true, action = action, data = data, undo = undo, toast = toast, changed = changed ?? new string[0] };

        internal static AgentResult Fail(string action, string code, string message, string hint = null)
            => new AgentResult { ok = false, action = action, code = code, message = message, hint = hint };

        internal static AgentResult Pending(string action, string message, object data = null)
            => new AgentResult { ok = true, pending = true, action = action, message = message, data = data };
    }
}
