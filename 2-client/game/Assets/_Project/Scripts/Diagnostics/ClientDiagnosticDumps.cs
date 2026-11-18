using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Pogranichye.Client.Diagnostics
{
    /// <summary>
    /// Bounded, opt-in local diagnostic capture for the Unity client.
    /// It records only world frames and short technical events; chat, voice,
    /// credentials and raw outbound intents are deliberately excluded.
    /// </summary>
    public static class ClientDiagnosticDumps
    {
        private const int MaxFullFrames = 8;
        private const int MaxFastFrames = 16;
        private const int MaxRejectedFrames = 3;
        private const int MaxEvents = 100;
        private const int MaxFrameBytes = 256 * 1024;
        // Character limit used by older dump capture call sites; keep it aligned with the byte cap.
        private const int MaxFrameChars = MaxFrameBytes;
        private const int MaxDumpFiles = 5;
        private const long MaxDumpBytes = 10L * 1024L * 1024L;

        private static readonly object Gate = new object();
        private static readonly List<JObject> FullFrames = new List<JObject>();
        private static readonly List<JObject> FastFrames = new List<JObject>();
        private static readonly List<JObject> RejectedFrames = new List<JObject>();
        private static readonly List<JObject> Events = new List<JObject>();

        public static void RecordFrame(string kind, string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            lock (Gate)
            {
                var target = kind == "fast" ? FastFrames :
                    kind == "rejected" ? RejectedFrames : FullFrames;
                var limit = kind == "fast" ? MaxFastFrames :
                    kind == "rejected" ? MaxRejectedFrames : MaxFullFrames;
                if (Encoding.UTF8.GetByteCount(json) > MaxFrameBytes)
                {
                    AddEventLocked("capture.skipped", kind + " frame exceeds " + MaxFrameBytes + " bytes");
                    return;
                }

                target.Add(new JObject
                {
                    ["utc"] = DateTime.UtcNow.ToString("o"),
                    ["kind"] = kind ?? "unknown",
                    ["json"] = json
                });
                while (target.Count > limit) target.RemoveAt(0);
            }
        }

        public static void RecordEvent(string category, string detail)
        {
            lock (Gate) AddEventLocked(category, detail);
        }

        private static void AddEventLocked(string category, string detail)
        {
            Events.Add(new JObject
            {
                ["utc"] = DateTime.UtcNow.ToString("o"),
                ["category"] = Limit(category, 64),
                ["detail"] = Limit(detail, 400)
            });
            while (Events.Count > MaxEvents) Events.RemoveAt(0);
        }

        public static string SaveDump(JObject clientContext)
        {
            JObject dump;
            lock (Gate)
            {
                dump = new JObject
                {
                    ["schema"] = "pogranichye.client-dump.v1",
                    ["generated_utc"] = DateTime.UtcNow.ToString("o"),
                    ["client"] = clientContext != null ? (JObject)clientContext.DeepClone() : new JObject(),
                    ["frames"] = new JObject
                    {
                        ["full"] = new JArray(FullFrames.ToArray()),
                        ["fast"] = new JArray(FastFrames.ToArray()),
                        ["rejected"] = new JArray(RejectedFrames.ToArray())
                    },
                    ["events"] = new JArray(Events.ToArray())
                };
            }

            var directory = Path.Combine(Application.persistentDataPath, "PogranichyeDiagnostics");
            Directory.CreateDirectory(directory);
            var fileName = "pogranichye-dump-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json";
            var path = Path.Combine(directory, fileName);
            var json = dump.ToString(Formatting.Indented);
            if (Encoding.UTF8.GetByteCount(json) > MaxDumpBytes)
                throw new InvalidOperationException("Диагностический дамп превысил ограничение размера.");
            File.WriteAllText(path, json, new UTF8Encoding(false));
            PruneOldDumps(directory);
            return path;
        }

        public static string SafeServerAddress(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
                return "(server URL omitted: invalid format)";
            var builder = new UriBuilder(uri)
            {
                UserName = "",
                Password = "",
                Query = "",
                Fragment = ""
            };
            return Limit(builder.Uri.ToString(), 200);
        }

        private static string Limit(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= max ? value : value.Substring(0, max);
        }

        private static void PruneOldDumps(string directory)
        {
            var files = new DirectoryInfo(directory).GetFiles("pogranichye-dump-*.json");
            Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            long total = 0;
            for (var i = 0; i < files.Length; i++)
            {
                var fileLength = files[i].Length;
                total += fileLength;
                if (i >= MaxDumpFiles || total > MaxDumpBytes)
                {
                    try { files[i].Delete(); total -= fileLength; }
                    catch (Exception ex) { Debug.LogWarning("[Pogranichye] Failed to delete an old diagnostic dump: " + ex.Message); }
                }
            }
        }
    }
}
