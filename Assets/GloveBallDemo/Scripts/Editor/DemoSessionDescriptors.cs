using System;
using System.IO;
using Hapbeat.DemoSwitch;
using UnityEditor;

namespace GloveBallDemo.Editor
{
    /// <summary>
    /// Volley and GloveBall APKs are built from one project and share StreamingAssets, but each needs
    /// its own Demo Session descriptor (different demo_id). The checked-in StreamingAssets file is the
    /// Volley descriptor; the GloveBall build swaps in its own for the duration of the build.
    /// </summary>
    public static class DemoSessionDescriptors
    {
        public const string StreamingPath = "Assets/StreamingAssets/" + DemoSessionDescriptor.FileName;
        public const string GloveBallSource = "Assets/GloveBallDemo/Session/gloveball-demo-session.json";
        public const string VolleyDemoId = "volley";
        public const string GloveBallDemoId = "gloveball_v2";

        /// <summary>
        /// Puts <paramref name="source"/> (null = keep the checked-in file) at the StreamingAssets path and
        /// verifies its demo_id. Disposing restores the checked-in file.
        /// </summary>
        public static IDisposable Use(string source, string expectedDemoId)
        {
            var original = File.ReadAllBytes(StreamingPath);
            var restore = new Restore(original);
            try
            {
                if (source != null)
                {
                    File.Copy(source, StreamingPath, true);
                    AssetDatabase.ImportAsset(StreamingPath, ImportAssetOptions.ForceSynchronousImport);
                }
                Require(StreamingPath, expectedDemoId);
                return restore;
            }
            catch
            {
                restore.Dispose();
                throw;
            }
        }

        public static DemoSessionDescriptor Require(string path, string expectedDemoId)
        {
            if (!DemoSessionDescriptor.TryParse(File.ReadAllText(path), out var descriptor, out var error))
                throw new InvalidOperationException("Invalid Demo Session descriptor " + path + ": " + error);
            if (descriptor.DemoId != expectedDemoId)
                throw new InvalidOperationException("Descriptor " + path + " has demo_id '" + descriptor.DemoId + "', expected '" + expectedDemoId + "'.");
            return descriptor;
        }

        private sealed class Restore : IDisposable
        {
            private readonly byte[] _original;
            private bool _done;

            public Restore(byte[] original)
            {
                _original = original;
            }

            public void Dispose()
            {
                if (_done) return;
                _done = true;
                File.WriteAllBytes(StreamingPath, _original);
                AssetDatabase.ImportAsset(StreamingPath, ImportAssetOptions.ForceSynchronousImport);
            }
        }
    }
}
