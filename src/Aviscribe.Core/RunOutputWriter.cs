using System;
using System.IO;
using System.Text;

namespace Aviscribe.Core
{
    public sealed class RunOutputWriter
    {
        public string OutputPath { get; set; } = AppPaths.PendingOutputPath;
        public GameLanguage Language { get; set; } = GameLanguage.English;

        public void WritePending(
            GameStateSnapshot snapshot,
            Func<Moon, bool>? include = null)
        {
            if (string.IsNullOrWhiteSpace(OutputPath))
                return;

            var directory = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var text = RunStateTextFormatter.FormatPending(
                snapshot,
                Language,
                include);
            var tempPath = $"{OutputPath}.{Guid.NewGuid():N}.tmp";

            File.WriteAllText(tempPath, text, Encoding.UTF8);
            File.Move(tempPath, OutputPath, overwrite: true);
        }
    }
}
