using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AstralOath.Client.FoundationDemo
{
    // Save the id before contacting the server, so a lost response or interrupted scene load can resume the same fight.
    internal sealed class TowerChallengeRequest
    {
        private readonly string preference;
        internal string Id { get; }

        private TowerChallengeRequest(string preference, string id)
        {
            this.preference = preference;
            Id = id;
        }

        internal static TowerChallengeRequest Resume(Uri server, string player, int stage, string[] heroes)
        {
            if (string.IsNullOrEmpty(player)) throw new InvalidOperationException("Missing authenticated player identity.");
            string identity = string.Join("\n", server.AbsoluteUri, player, stage.ToString(CultureInfo.InvariantCulture), string.Join(",", heroes));
            using var sha = SHA256.Create();
            string preference = "arena.tower.request." + Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(identity)));
            string id = PlayerPrefs.GetString(preference, "");
            if (!Guid.TryParseExact(id, "N", out _))
            {
                id = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(preference, id);
                PlayerPrefs.Save();
            }
            return new TowerChallengeRequest(preference, id);
        }

        internal static bool IsRejected(string code) => code is "invalid_tower_challenge" or "tower_stage_not_found"
            or "tower_stage_locked" or "request_conflict" or "trial_formation_invalid" or "trial_hero_not_found"
            or "invalid_realm" or "battle_content_mismatch";

        internal void Complete()
        {
            if (PlayerPrefs.GetString(preference, "") != Id) return;
            PlayerPrefs.DeleteKey(preference);
            PlayerPrefs.Save();
        }
    }
}
