using UnityEditor;
using UnityEngine;

namespace CriticalMass.Editor.Art
{
    /// <summary>
    /// Audio load types (technical hardening, 2026-11-12). Short sounds (Audio/Resources/CMSfx and any other clip not under
    /// a Music folder) decompress on load and preload, so the first gunshot or explosion never waits on disk or a decoder.
    /// Music (any path with /Music/) streams from disk as Vorbis, so a 3-minute track costs a small buffer instead of tens
    /// of megabytes of decoded audio (budget: 24 MB decoded audio, DG-60/plan section 3). Enforced on every import.
    /// Channels and sample rate are left as authored (forcing mono would change how the mix sounds).
    /// </summary>
    public sealed class AudioImportRules : AssetPostprocessor
    {
        /// <summary>true for music paths (pure; tests).</summary>
        public static bool IsMusic(string path) => path.Replace('\\', '/').Contains("/Music/");

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/_Game/")) return;
            var ai = (AudioImporter)assetImporter;
            AudioImporterSampleSettings s = ai.defaultSampleSettings;
            if (IsMusic(assetPath))
            {
                s.loadType = AudioClipLoadType.Streaming;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.6f;
                s.preloadAudioData = false;
                ai.loadInBackground = true;
            }
            else
            {
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.6f;
                s.preloadAudioData = true;
                ai.loadInBackground = false;
            }
            ai.defaultSampleSettings = s;
        }
    }
}
