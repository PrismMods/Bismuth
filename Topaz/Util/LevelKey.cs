using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Topaz
{
    /* Resolves a stable per-level key for attempts tracking.

       Official levels: scrController.levelName (= GCS.internalLevelName, e.g. "1-1")
       is already stable, so it is used verbatim.

       Custom levels previously keyed on scnGame.instance.levelPath, which breaks
       attempt continuity whenever the .adofai file is moved/renamed/re-downloaded,
       and collides if a different chart later occupies the same path. Instead we
       hash the loaded LevelData's metadata + tile sequence:
         - survives file moves/renames (no path in the key)
         - survives offset/decoration/metadata edits (don't touch the sequence)
         - distinguishes charts that share a song (different angleData/pathData)

       NOTE: the game's own LevelData.get_Hash() is MD5(author+artist+song) ONLY --
       it has no tile data, so it collides for same-song charts. Do not reuse it. */
    internal struct LevelKey
    {
        public string Key;        // primary key to store attempts under
        public string LegacyKey;  // previous path-based key for one-time migration, or null

        private const string CustomPrefix = "C::";  // namespaces content keys from level names / FullPrefix

        public static LevelKey Resolve()
        {
            var controller = scrController.instance;
            if (controller == null) return default(LevelKey);

            string name = controller.levelName;
            // Official levels: stable internal name. No migration needed.
            if (!string.IsNullOrEmpty(name) && name != "scnGame")
                return new LevelKey { Key = name, LegacyKey = null };

            // Custom level. Legacy key was the raw .adofai path.
            var game = scnGame.instance;
            string path = game != null ? game.levelPath : null;
            string legacy = string.IsNullOrEmpty(path) ? null : path;

            string content = ContentKey(game);
            // If the chart couldn't be read (e.g. too early), fall back to the path
            // and skip migration (LegacyKey == Key would be a no-op anyway).
            if (content == null) return new LevelKey { Key = legacy, LegacyKey = null };
            return new LevelKey { Key = content, LegacyKey = legacy };
        }

        private static string ContentKey(scnGame game)
        {
            try
            {
                var ld = game != null ? game.levelData : null;
                if (ld == null) return null;

                string song = ld.song ?? "";
                string artist = ld.artist ?? "";
                string author = ld.author ?? "";
                string seq = TileSequence(ld);

                // Need at least something identifying; otherwise let caller use the path.
                if (song.Length == 0 && artist.Length == 0 && author.Length == 0 && seq.Length == 0)
                    return null;

                // Length-prefix every field so distinct tuples can never alias to the
                // same concatenation (no separator char that could appear in a field).
                var sb = new StringBuilder(seq.Length + 64);
                AppendField(sb, author);
                AppendField(sb, artist);
                AppendField(sb, song);
                AppendField(sb, seq);
                return CustomPrefix + Sha256Hex(sb.ToString());
            }
            catch (Exception ex)
            {
                TopazLog.Debug("LevelKey.ContentKey failed: " + ex.Message);
                return null;
            }
        }

        private static void AppendField(StringBuilder sb, string s)
        {
            sb.Append(s.Length).Append(':').Append(s);
        }

        /* Modern levels store the tile layout as angleData (List<float>); legacy
           levels use pathData (letter string). Hash whichever is populated. Floats
           are formatted round-trip + invariant so the key is byte-stable per chart. */
        private static string TileSequence(ADOFAI.LevelData ld)
        {
            var angles = ld.angleData;
            if (angles != null && angles.Count > 0)
            {
                var sb = new StringBuilder(angles.Count * 6);
                for (int i = 0; i < angles.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(angles[i].ToString("R", CultureInfo.InvariantCulture));
                }
                return sb.ToString();
            }
            return ld.pathData ?? "";
        }

        private static string Sha256Hex(string s)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
