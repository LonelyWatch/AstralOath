using System.Collections.Generic;
using AstralOath.Client.Core;

namespace AstralOath.Client.Arena
{
    internal static class ArenaError
    {
        internal static string Text(ILocaleService locale, ArenaRequestException error)
        {
            try { return locale.Resolve("arena.error." + error.Code); }
            catch (KeyNotFoundException) { return locale.Resolve("arena.error.request_failed"); }
        }
    }
}
