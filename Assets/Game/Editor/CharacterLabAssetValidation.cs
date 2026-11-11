using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace AstralOath.Editor
{
    // Pure JSON checks shared by the Editor build gate and standalone validation.
    public static class CharacterLabAssetValidation
    {
        private const string RequiredUiKeys = "lab.title lab.close lab.subtitle lab.search lab.animations lab.voices lab.skills lab.diagnostics lab.battle lab.portrait lab.play lab.pause lab.stop lab.step lab.loop lab.on lab.off lab.slower lab.faster lab.zoom_in lab.zoom_out lab.flip lab.background lab.reset lab.skin lab.skin_empty lab.loading lab.ready lab.missing lab.error lab.transport lab.sound lab.silent lab.raw lab.voice_status lab.unavailable lab.choose_animation lab.empty lab.skill lab.skill_diagnostic lab.not_supported lab.selection lab.targets lab.experimental lab.experimental_warning lab.seek_back lab.seek_forward";
        public static void Validate(string catalogJson, string localizationJson, IReadOnlyDictionary<string, string> resources,
            Func<string, string> readResourceJson)
        {
            if (resources == null) throw new ArgumentNullException(nameof(resources));
            if (readResourceJson == null) throw new ArgumentNullException(nameof(readResourceJson));
            var catalog = Parse(catalogJson, "catalog");
            var localization = Parse(localizationJson, "localization");
            if (catalog["schemaVersion"]?.Type != JTokenType.Integer || (long)catalog["schemaVersion"] != 1)
                Fail("catalog.schemaVersion must be 1");
            var localeKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in Rows(localization, "rows", "localization", true))
            {
                var item = Object(row, "localization.rows");
                string key = Text(item, "key", "localization.rows");
                Text(item, "zhCn", "localization." + key);
                if (!localeKeys.Add(key)) Fail("Duplicate localization key: " + key);
            }
            foreach (string key in RequiredUiKeys.Split(' ')) RequireLocale(key, localeKeys);
            var sharedBanks = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in Rows(catalog, "sharedBanks", "catalog", true))
            {
                if (token.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)token)) Fail("Invalid shared bank address");
                string bank = (string)token;
                RequireResource(bank, "bank", resources);
                if (!sharedBanks.Add(bank)) Fail("Duplicate shared bank: " + bank);
            }
            var characterIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in Rows(catalog, "characters", "catalog", true))
            {
                var character = Object(token, "characters");
                string id = Text(character, "id", "character");
                if (!characterIds.Add(id)) Fail("Duplicate character id: " + id);
                RequireLocale(Text(character, "displayNameKey", id), localeKeys);
                string battleSkeleton = Text(character, "battleSkeletonAddress", id);
                string portraitSkeleton = Text(character, "portraitSkeletonAddress", id);
                RequireResource(battleSkeleton, "skeleton", resources);
                RequireResource(portraitSkeleton, "skeleton", resources);
                RequireSkin(Text(character, "battleSkin", id), battleSkeleton, id + ".battleSkin", resources, readResourceJson);
                RequireSkin(Text(character, "portraitSkin", id), portraitSkeleton, id + ".portraitSkin", resources, readResourceJson);
                string voiceBank = Text(character, "voiceBankAddress", id);
                RequireResource(voiceBank, "bank", resources);
                if (!sharedBanks.Contains(voiceBank)) Fail("Voice bank is not loaded by sharedBanks: " + voiceBank);
                string timeline = Text(character, "timelineAddress", id, false);
                if (timeline.Length != 0) RequireResource(timeline, "timeline", resources);
                var voiceIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var voiceToken in Rows(character, "voices", id, false))
                {
                    var voice = Object(voiceToken, id + ".voices");
                    string voiceId = Text(voice, "id", id + ".voice");
                    if (!voiceIds.Add(voiceId)) Fail("Duplicate voice id in " + id + ": " + voiceId);
                    RequireLocale(Text(voice, "displayNameKey", voiceId), localeKeys);
                    string tableKey = Text(voice, "audioTableKey", voiceId, false);
                    string eventPath = Text(voice, "eventPath", voiceId, false);
                    if (string.IsNullOrWhiteSpace(tableKey) && string.IsNullOrWhiteSpace(eventPath))
                        Fail("Voice has neither audioTableKey nor eventPath: " + voiceId);
                }
                var skillIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var skillToken in Rows(character, "skills", id, false))
                {
                    var skill = Object(skillToken, id + ".skills");
                    string skillId = Text(skill, "id", id + ".skill");
                    if (!skillIds.Add(skillId)) Fail("Duplicate skill id in " + id + ": " + skillId);
                    RequireLocale(Text(skill, "displayNameKey", skillId), localeKeys);
                    RequireResource(Text(skill, "graphAddress", skillId), "graph", resources);
                    Text(skill, "eventPath", skillId, false);
                    Text(skill, "voiceEventPath", skillId, false);
                }
            }
        }
        private static void RequireSkin(string skin, string skeletonAddress, string context,
            IReadOnlyDictionary<string, string> resources, Func<string, string> readResourceJson)
        {
            var descriptor = Parse(readResourceJson(skeletonAddress), context + " descriptor");
            string jsonAddress = Text(descriptor, "jsonAddress", context + " descriptor");
            RequireResource(jsonAddress, "spine-json", resources);
            var skeleton = Parse(readResourceJson(jsonAddress), context + " skeleton");
            bool found = false;
            // Spine 2.1 stores skins by object key; 3.8 stores named objects in an array.
            if (skeleton["skins"] is JObject legacy)
                found = legacy.Property(skin, StringComparison.Ordinal)?.Value is JObject;
            else if (skeleton["skins"] is JArray modern)
                foreach (var token in modern)
                    if (Text(Object(token, context + " skins"), "name", context + " skins") == skin) found = true;
            if (!found) Fail(context + " references unknown skin '" + skin + "' in " + skeletonAddress);
        }

        private static JObject Parse(string json, string context)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException(context + " is empty");
            try { return JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }); }
            catch (JsonException error) { throw new InvalidDataException("Invalid " + context + " JSON", error); }
        }
        private static JObject Object(JToken token, string context) => token as JObject ?? throw new InvalidDataException(context + " must contain objects");
        private static JArray Rows(JObject value, string field, string context, bool nonempty)
        {
            if (!(value[field] is JArray rows) || (nonempty && rows.Count == 0))
                throw new InvalidDataException(context + "." + field + " must be " + (nonempty ? "a nonempty array" : "an array"));
            return rows;
        }
        private static string Text(JObject value, string field, string context, bool required = true)
        {
            var token = value[field];
            if (!required && (token == null || token.Type == JTokenType.Null)) return "";
            if (token?.Type != JTokenType.String || (required && string.IsNullOrWhiteSpace((string)token)))
                throw new InvalidDataException(context + "." + field + " must be a nonempty string");
            return (string)token;
        }
        private static void RequireLocale(string key, HashSet<string> keys)
        { if (!keys.Contains(key)) Fail("Missing localization key: " + key); }
        private static void RequireResource(string address, string kind, IReadOnlyDictionary<string, string> resources)
        {
            if (!resources.TryGetValue(address, out var actual)) Fail("Missing resource address: " + address);
            if (actual != kind) Fail("Expected " + kind + " resource at " + address + ", found " + actual);
        }
        private static void Fail(string message) => throw new InvalidDataException(message);
    }
}
