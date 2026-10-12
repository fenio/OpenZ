using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenZ.Sim
{
    /// <summary>One unit's settings block (zsettings.h:17-44), ints and doubles typed like Zod.</summary>
    public sealed class ZUnitSettings
    {
        public string name;
        public int group_amount, move_speed, attack_radius, attack_damage_radius, attack_missile_speed, build_time;
        public double attack_damage, attack_damage_chance, attack_speed, attack_snipe_chance, health, max_run_time;

        // zsettings.cpp:292-330
        public void CensorNegatives()
        {
            if (group_amount < 0) group_amount = 0; if (move_speed < 0) move_speed = 0; if (attack_radius < 0) attack_radius = 0;
            if (attack_damage < 0) attack_damage = 0; if (attack_damage_chance < 0) attack_damage_chance = 0;
            if (attack_damage_radius < 0) attack_damage_radius = 0; if (attack_missile_speed < 0) attack_missile_speed = 0;
            if (attack_speed < 0) attack_speed = 0; if (attack_snipe_chance < 0) attack_snipe_chance = 0; if (health < 0) health = 0;
            if (build_time < 0) build_time = 0;
            if (attack_damage_chance > 1.0) attack_damage_chance = 1.0; if (attack_snipe_chance > 1.0) attack_snipe_chance = 1.0;
        }
        public void CensorNonMissileUnit() { attack_damage_radius = 0; attack_missile_speed = 0; }
        public void CensorMissileUnit() { attack_damage_chance = 0; }

        public int MaxHealth => (int)(health * ZConst.MAX_UNIT_HEALTH);        // zobject.cpp:189 (truncates)
        public int Damage => (int)(attack_damage * ZConst.MAX_UNIT_HEALTH);    // zobject.cpp:183
    }

    /// <summary>
    /// Parses default_settings.txt exactly like zsettings.cpp (key=value, "unit.&lt;name&gt;.&lt;field&gt;", "building.*", "map_item.*",
    /// "global.global.*"), then applies CensorSettings (zsettings.cpp:332-390). Every raw key is kept in <see cref="Raw"/>.
    /// </summary>
    public sealed class ZSettings
    {
        public static readonly string[] Robots = { "grunt", "psycho", "sniper", "tough", "pyro", "laser", "electrobot" };   // electrobot_v1 DV45: OpenZ's seventh robot (not in Z)
        public static readonly string[] Vehicles = { "jeep", "light", "medium", "heavy", "apc", "missile_launcher", "crane" };
        public static readonly string[] Cannons = { "gatling", "gun", "howitzer", "missile_cannon" };

        public readonly Dictionary<string, ZUnitSettings> Units = new Dictionary<string, ZUnitSettings>();
        public readonly Dictionary<string, double> Raw = new Dictionary<string, double>();

        public double fort_building_health, robot_building_health, vehicle_building_health, repair_building_health,
                      radar_building_health, bridge_building_health;
        public double rock_item_health, grenades_item_health, rockets_item_health, hut_item_health, map_item_health;
        public double grenade_damage, grenade_attack_speed, map_item_turrent_damage;
        public int grenade_damage_radius, grenade_missile_speed;
        public int agro_distance, auto_grab_vehicle_distance, auto_grab_flag_distance, building_auto_repair_time,
                   building_auto_repair_random_additional_time, max_turrent_horizontal_distance, max_turrent_vertical_distance,
                   grenades_per_box, hut_animal_max, hut_animal_min, hut_animal_roam_distance;
        public double partially_damaged_unit_speed, damaged_unit_speed, run_unit_speed, run_recharge_rate;

        public ZUnitSettings Unit(string name) => Units.TryGetValue(name, out var u) ? u : null;

        public static ZSettings Parse(string text)
        {
            var s = new ZSettings();
            foreach (var n in Robots) s.Units[n] = new ZUnitSettings { name = n };
            foreach (var n in Vehicles) s.Units[n] = new ZUnitSettings { name = n };
            foreach (var n in Cannons) s.Units[n] = new ZUnitSettings { name = n };
            foreach (var line0 in text.Split('\n'))
            {
                var line = line0.Trim();
                int eq = line.IndexOf('=');
                if (line.Length == 0 || eq <= 0) continue;
                string key = line.Substring(0, eq).Trim(), val = line.Substring(eq + 1).Trim();
                if (!double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) continue;
                s.Raw[key] = d;
                var parts = key.Split('.');
                if (parts.Length != 3) continue;
                if (parts[0] == "unit" && s.Units.TryGetValue(parts[1], out var u)) SetUnitField(u, parts[2], d);
            }
            s.Globals();
            s.CensorSettings();
            return s;
        }

        static int Atoi(double d) => (int)d;   // int fields are written as integers; C atoi truncates

        static void SetUnitField(ZUnitSettings u, string f, double d)
        {
            switch (f)
            {
                case "group_amount": u.group_amount = Atoi(d); break;
                case "move_speed": u.move_speed = Atoi(d); break;
                case "attack_radius": u.attack_radius = Atoi(d); break;
                case "attack_damage": u.attack_damage = d; break;
                case "attack_damage_chance": u.attack_damage_chance = d; break;
                case "attack_damage_radius": u.attack_damage_radius = Atoi(d); break;
                case "attack_missile_speed": u.attack_missile_speed = Atoi(d); break;
                case "attack_speed": u.attack_speed = d; break;
                case "attack_snipe_chance": u.attack_snipe_chance = d; break;
                case "health": u.health = d; break;
                case "build_time": u.build_time = Atoi(d); break;
                case "max_run_time": u.max_run_time = d; break;
            }
        }

        double R(string k) => Raw.TryGetValue(k, out var v) ? v : 0;

        void Globals()
        {
            fort_building_health = R("building.fort.health"); robot_building_health = R("building.robot.health");
            vehicle_building_health = R("building.vehicle.health"); repair_building_health = R("building.repair.health");
            radar_building_health = R("building.radar.health"); bridge_building_health = R("building.bridge.health");
            rock_item_health = R("map_item.rock.health"); grenades_item_health = R("map_item.grenades.health");
            rockets_item_health = R("map_item.rockets.health"); hut_item_health = R("map_item.hut.health");
            map_item_health = R("map_item.map_item.health");
            grenade_damage = R("map_item.grenades.grenade_damage"); grenade_damage_radius = Atoi(R("map_item.grenades.grenade_damage_radius"));
            grenade_missile_speed = Atoi(R("map_item.grenades.grenade_missile_speed")); grenade_attack_speed = R("map_item.grenades.grenade_attack_speed");
            map_item_turrent_damage = R("map_item.map_item.map_item_turrent_damage");
            string g = "global.global.";
            agro_distance = Atoi(R(g + "agro_distance")); auto_grab_vehicle_distance = Atoi(R(g + "auto_grab_vehicle_distance"));
            auto_grab_flag_distance = Atoi(R(g + "auto_grab_flag_distance")); building_auto_repair_time = Atoi(R(g + "building_auto_repair_time"));
            building_auto_repair_random_additional_time = Atoi(R(g + "building_auto_repair_random_additional_time"));
            max_turrent_horizontal_distance = Atoi(R(g + "max_turrent_horizontal_distance"));
            max_turrent_vertical_distance = Atoi(R(g + "max_turrent_vertical_distance"));
            grenades_per_box = Atoi(R(g + "grenades_per_box"));
            partially_damaged_unit_speed = R(g + "partially_damaged_unit_speed"); damaged_unit_speed = R(g + "damaged_unit_speed");
            run_unit_speed = R(g + "run_unit_speed"); run_recharge_rate = R(g + "run_recharge_rate");
            hut_animal_max = Atoi(R(g + "hut_animal_max")); hut_animal_min = Atoi(R(g + "hut_animal_min"));
            hut_animal_roam_distance = Atoi(R(g + "hut_animal_roam_distance"));
        }

        /// <summary>zsettings.cpp:332-390</summary>
        void CensorSettings()
        {
            foreach (var n in new[] { "grunt", "psycho", "sniper", "pyro", "laser", "jeep", "gatling", "electrobot" }) Units[n].CensorNonMissileUnit();   // electrobot_v1: a direct hit
            foreach (var n in new[] { "tough", "light", "medium", "heavy", "missile_launcher", "gun", "howitzer", "missile_cannon" }) Units[n].CensorMissileUnit();
            Units["apc"].CensorMissileUnit(); Units["crane"].CensorMissileUnit();          // "non weapon" block calls CensorMissileUnit in Zod
            foreach (var n in Cannons) Units[n].move_speed = 0;
            foreach (var u in Units.Values) u.CensorNegatives();
            if (agro_distance < 0) agro_distance = 0; if (auto_grab_vehicle_distance < 0) auto_grab_vehicle_distance = 0;
            if (auto_grab_flag_distance < 0) auto_grab_flag_distance = 0; if (building_auto_repair_time < 0) building_auto_repair_time = 0;
            if (building_auto_repair_random_additional_time < 0) building_auto_repair_random_additional_time = 0;
            if (max_turrent_horizontal_distance < 0) max_turrent_horizontal_distance = 0; if (max_turrent_vertical_distance < 0) max_turrent_vertical_distance = 0;
            if (grenades_per_box < 0) grenades_per_box = 0; if (grenades_per_box > 99) grenades_per_box = 99;
            if (grenade_damage < 0) grenade_damage = 0; if (grenade_damage_radius < 0) grenade_damage_radius = 0;
            if (map_item_turrent_damage < 0) map_item_turrent_damage = 0;
            if (partially_damaged_unit_speed < 0) partially_damaged_unit_speed = 0; if (damaged_unit_speed < 0) damaged_unit_speed = 0;
            if (run_unit_speed < 0) run_unit_speed = 0;
            if (run_recharge_rate < 0) run_recharge_rate = 0; if (run_recharge_rate > 1) run_recharge_rate = 1;
            if (hut_animal_max < 0) hut_animal_max = 0; if (hut_animal_min < 0) hut_animal_min = 0;
            if (hut_animal_roam_distance < 0) hut_animal_roam_distance = 0;
        }
    }
}
