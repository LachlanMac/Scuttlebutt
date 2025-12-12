using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace Starbelter.Ship
{
    /// <summary>
    /// Manages all weapons on a ship. Handles grouping and firing.
    /// </summary>
    public class WeaponSystem : MonoBehaviour
    {
        // All weapon mounts on this ship
        private List<WeaponMount> allWeapons = new List<WeaponMount>();

        // Grouped by control type and group number
        private Dictionary<int, List<WeaponMount>> pilotGroups = new Dictionary<int, List<WeaponMount>>();
        private Dictionary<int, List<WeaponMount>> officerGroups = new Dictionary<int, List<WeaponMount>>();
        private List<WeaponMount> automatedWeapons = new List<WeaponMount>();

        // Properties
        public IReadOnlyList<WeaponMount> AllWeapons => allWeapons;

        void Awake()
        {
            DiscoverWeapons();
        }

        /// <summary>
        /// Find all WeaponMount components on this ship.
        /// </summary>
        public void DiscoverWeapons()
        {
            allWeapons.Clear();
            pilotGroups.Clear();
            officerGroups.Clear();
            automatedWeapons.Clear();

            allWeapons.AddRange(GetComponentsInChildren<WeaponMount>());

            foreach (var weapon in allWeapons)
            {
                switch (weapon.ControlType)
                {
                    case WeaponControlType.Pilot:
                        if (!pilotGroups.ContainsKey(weapon.WeaponGroup))
                            pilotGroups[weapon.WeaponGroup] = new List<WeaponMount>();
                        pilotGroups[weapon.WeaponGroup].Add(weapon);
                        break;

                    case WeaponControlType.WeaponsOfficer:
                        if (!officerGroups.ContainsKey(weapon.WeaponGroup))
                            officerGroups[weapon.WeaponGroup] = new List<WeaponMount>();
                        officerGroups[weapon.WeaponGroup].Add(weapon);
                        break;

                    case WeaponControlType.Automated:
                        automatedWeapons.Add(weapon);
                        break;
                }
            }

            Debug.Log($"[WeaponSystem] Discovered {allWeapons.Count} weapons: " +
                $"{pilotGroups.Values.Sum(g => g.Count)} pilot, " +
                $"{officerGroups.Values.Sum(g => g.Count)} officer, " +
                $"{automatedWeapons.Count} automated");
        }

        #region Pilot Weapons

        /// <summary>
        /// Fire all pilot-controlled weapons in a group.
        /// </summary>
        public void FirePilotGroup(int group)
        {
            if (!pilotGroups.TryGetValue(group, out var weapons))
                return;

            foreach (var weapon in weapons)
            {
                weapon.Fire();
            }
        }

        /// <summary>
        /// Fire all pilot-controlled weapons (all groups).
        /// </summary>
        public void FireAllPilotWeapons()
        {
            foreach (var group in pilotGroups.Values)
            {
                foreach (var weapon in group)
                {
                    weapon.Fire();
                }
            }
        }

        /// <summary>
        /// Get available pilot weapon groups.
        /// </summary>
        public int[] GetPilotGroups()
        {
            return pilotGroups.Keys.OrderBy(k => k).ToArray();
        }

        #endregion

        #region Weapons Officer Weapons

        /// <summary>
        /// Fire all weapons officer-controlled weapons in a group.
        /// </summary>
        public void FireOfficerGroup(int group)
        {
            if (!officerGroups.TryGetValue(group, out var weapons))
                return;

            foreach (var weapon in weapons)
            {
                weapon.Fire();
            }
        }

        /// <summary>
        /// Set target for all weapons officer weapons in a group.
        /// </summary>
        public void SetOfficerGroupTarget(int group, Transform target)
        {
            if (!officerGroups.TryGetValue(group, out var weapons))
                return;

            foreach (var weapon in weapons)
            {
                weapon.SetTarget(target);
            }
        }

        /// <summary>
        /// Aim all weapons officer weapons in a group at a position.
        /// </summary>
        public void AimOfficerGroup(int group, Vector3 position)
        {
            if (!officerGroups.TryGetValue(group, out var weapons))
                return;

            foreach (var weapon in weapons)
            {
                weapon.AimAt(position);
            }
        }

        /// <summary>
        /// Get available weapons officer groups.
        /// </summary>
        public int[] GetOfficerGroups()
        {
            return officerGroups.Keys.OrderBy(k => k).ToArray();
        }

        #endregion

        #region Queries

        /// <summary>
        /// Get all weapons of a specific control type.
        /// </summary>
        public List<WeaponMount> GetWeaponsByControlType(WeaponControlType type)
        {
            switch (type)
            {
                case WeaponControlType.Pilot:
                    return pilotGroups.Values.SelectMany(g => g).ToList();
                case WeaponControlType.WeaponsOfficer:
                    return officerGroups.Values.SelectMany(g => g).ToList();
                case WeaponControlType.Automated:
                    return new List<WeaponMount>(automatedWeapons);
                default:
                    return new List<WeaponMount>();
            }
        }

        /// <summary>
        /// Get a specific weapon by name.
        /// </summary>
        public WeaponMount GetWeaponByName(string name)
        {
            return allWeapons.FirstOrDefault(w => w.WeaponName == name);
        }

        /// <summary>
        /// Check if any weapon in a pilot group can fire.
        /// </summary>
        public bool CanFirePilotGroup(int group)
        {
            if (!pilotGroups.TryGetValue(group, out var weapons))
                return false;

            return weapons.Any(w => w.CanFire());
        }

        /// <summary>
        /// Check if any weapon in an officer group can fire.
        /// </summary>
        public bool CanFireOfficerGroup(int group)
        {
            if (!officerGroups.TryGetValue(group, out var weapons))
                return false;

            return weapons.Any(w => w.CanFire());
        }

        #endregion

        #region Ammo

        /// <summary>
        /// Reload all weapons.
        /// </summary>
        public void ReloadAll()
        {
            foreach (var weapon in allWeapons)
            {
                weapon.Reload();
            }
        }

        /// <summary>
        /// Reload weapons in a specific group.
        /// </summary>
        public void ReloadPilotGroup(int group)
        {
            if (!pilotGroups.TryGetValue(group, out var weapons))
                return;

            foreach (var weapon in weapons)
            {
                weapon.Reload();
            }
        }

        #endregion
    }
}
