// Generated from layout-official-v1.json. Proposed geometry; not runtime enabled.
using System;
using System.Collections.ObjectModel;
using System.Drawing;
namespace Client.Models.GameUi
{
    public static class OfficialUiLayoutContract
    {
        public const bool ProductionBindingAllowed = false;
        public const string SourceSha256 = "496f406266adb07da791fb677cb456abc499cd395fe43237be297c41bc6833c0";
        public sealed class Node
        {
            public readonly string Id, Owner, Page, Kind, Binding;
            public readonly Rectangle Rect;
            public readonly Rectangle? HitRect;
            public Node(string id, string owner, string page, string kind, string binding, Rectangle rect, Rectangle? hit)
            { Id=id; Owner=owner; Page=page; Kind=kind; Binding=binding; Rect=rect; HitRect=hit; }
        }
        public static readonly ReadOnlyCollection<Node> Nodes = new ReadOnlyCollection<Node>(new Node[]
        {
            new Node("character.close", "character", "chrome", "control", "CloseButton", new Rectangle(386, 10, 18, 18), new Rectangle(386, 10, 18, 18)),
            new Node("character.tab.character", "character", "chrome", "control", "", new Rectangle(8, 38, 97, 24), new Rectangle(8, 38, 97, 24)),
            new Node("character.tab.extraEquipment", "character", "chrome", "control", "", new Rectangle(107, 38, 97, 24), new Rectangle(107, 38, 97, 24)),
            new Node("character.tab.status", "character", "chrome", "control", "", new Rectangle(206, 38, 97, 24), new Rectangle(206, 38, 97, 24)),
            new Node("character.tab.hermit", "character", "chrome", "control", "", new Rectangle(305, 38, 97, 24), new Rectangle(305, 38, 97, 24)),
            new Node("character.preview", "character", "character", "preview", "CharacterTab_BeforeChildrenDraw", new Rectangle(60, 138, 134, 193), null),
            new Node("character.equipment.Weapon", "character", "character", "control", "Grid[0]", new Rectangle(16, 174, 36, 36), new Rectangle(16, 174, 36, 36)),
            new Node("character.equipment.Armour", "character", "character", "control", "Grid[1]", new Rectangle(200, 174, 36, 36), new Rectangle(200, 174, 36, 36)),
            new Node("character.equipment.Helmet", "character", "character", "control", "Grid[2]", new Rectangle(16, 96, 36, 36), new Rectangle(16, 96, 36, 36)),
            new Node("character.equipment.Torch", "character", "extraEquipment", "control", "Grid[3]", new Rectangle(28, 130, 36, 36), new Rectangle(28, 130, 36, 36)),
            new Node("character.equipment.Torch.caption", "character", "extraEquipment", "caption", "", new Rectangle(14, 170, 64, 20), null),
            new Node("character.equipment.Necklace", "character", "character", "control", "Grid[4]", new Rectangle(200, 96, 36, 36), new Rectangle(200, 96, 36, 36)),
            new Node("character.equipment.BraceletL", "character", "character", "control", "Grid[5]", new Rectangle(16, 228, 36, 36), new Rectangle(16, 228, 36, 36)),
            new Node("character.equipment.BraceletR", "character", "character", "control", "Grid[6]", new Rectangle(200, 228, 36, 36), new Rectangle(200, 228, 36, 36)),
            new Node("character.equipment.RingL", "character", "character", "control", "Grid[7]", new Rectangle(16, 282, 36, 36), new Rectangle(16, 282, 36, 36)),
            new Node("character.equipment.RingR", "character", "character", "control", "Grid[8]", new Rectangle(200, 282, 36, 36), new Rectangle(200, 282, 36, 36)),
            new Node("character.equipment.Shoes", "character", "character", "control", "Grid[9]", new Rectangle(110, 340, 36, 36), new Rectangle(110, 340, 36, 36)),
            new Node("character.equipment.Poison", "character", "extraEquipment", "control", "Grid[10]", new Rectangle(124, 130, 36, 36), new Rectangle(124, 130, 36, 36)),
            new Node("character.equipment.Poison.caption", "character", "extraEquipment", "caption", "", new Rectangle(110, 170, 64, 20), null),
            new Node("character.equipment.Amulet", "character", "extraEquipment", "control", "Grid[11]", new Rectangle(220, 130, 36, 36), new Rectangle(220, 130, 36, 36)),
            new Node("character.equipment.Amulet.caption", "character", "extraEquipment", "caption", "", new Rectangle(206, 170, 64, 20), null),
            new Node("character.equipment.Flower", "character", "extraEquipment", "control", "Grid[12]", new Rectangle(316, 130, 36, 36), new Rectangle(316, 130, 36, 36)),
            new Node("character.equipment.Flower.caption", "character", "extraEquipment", "caption", "", new Rectangle(302, 170, 64, 20), null),
            new Node("character.equipment.HorseArmour", "character", "extraEquipment", "control", "Grid[13]", new Rectangle(28, 230, 36, 36), new Rectangle(28, 230, 36, 36)),
            new Node("character.equipment.HorseArmour.caption", "character", "extraEquipment", "caption", "", new Rectangle(14, 270, 64, 20), null),
            new Node("character.equipment.Emblem", "character", "extraEquipment", "control", "Grid[14]", new Rectangle(124, 230, 36, 36), new Rectangle(124, 230, 36, 36)),
            new Node("character.equipment.Emblem.caption", "character", "extraEquipment", "caption", "", new Rectangle(110, 270, 64, 20), null),
            new Node("character.equipment.Shield", "character", "extraEquipment", "control", "Grid[15]", new Rectangle(220, 230, 36, 36), new Rectangle(220, 230, 36, 36)),
            new Node("character.equipment.Shield.caption", "character", "extraEquipment", "caption", "", new Rectangle(206, 270, 64, 20), null),
            new Node("character.caption.new.level", "character", "character", "caption", "", new Rectangle(254, 98, 54, 20), null),
            new Node("character.caption.new.experience", "character", "character", "caption", "", new Rectangle(254, 122, 54, 20), null),
            new Node("character.caption.new.health", "character", "character", "caption", "", new Rectangle(254, 146, 54, 20), null),
            new Node("character.caption.new.mana", "character", "character", "caption", "", new Rectangle(254, 170, 54, 20), null),
            new Node("character.caption.existing.HandWeightLabel", "character", "character", "caption", "", new Rectangle(254, 194, 54, 20), null),
            new Node("character.caption.existing.WearWeightLabel", "character", "character", "caption", "", new Rectangle(254, 218, 54, 20), null),
            new Node("character.caption.DisplayStats.Accuracy", "character", "character", "caption", "", new Rectangle(254, 242, 54, 20), null),
            new Node("character.caption.DisplayStats.Agility", "character", "character", "caption", "", new Rectangle(254, 266, 54, 20), null),
            new Node("character.caption.DisplayStats.Comfort", "character", "character", "caption", "", new Rectangle(254, 290, 54, 20), null),
            new Node("character.caption.DisplayStats.Luck", "character", "character", "caption", "", new Rectangle(254, 314, 54, 20), null),
            new Node("character.caption.DisplayStats.MaxDC", "character", "character", "caption", "", new Rectangle(16, 396, 42, 20), null),
            new Node("character.caption.DisplayStats.MaxMC", "character", "character", "caption", "", new Rectangle(142, 396, 42, 20), null),
            new Node("character.caption.DisplayStats.MaxSC", "character", "character", "caption", "", new Rectangle(268, 396, 42, 20), null),
            new Node("character.caption.DisplayStats.MaxAC", "character", "character", "caption", "", new Rectangle(16, 420, 42, 20), null),
            new Node("character.caption.DisplayStats.MaxMR", "character", "character", "caption", "", new Rectangle(142, 420, 42, 20), null),
            new Node("character.caption.AttackStats", "character", "character", "caption", "", new Rectangle(16, 450, 60, 22), null),
            new Node("character.caption.AdvantageStats", "character", "character", "caption", "", new Rectangle(16, 476, 60, 22), null),
            new Node("character.caption.DisadvantageStats", "character", "character", "caption", "", new Rectangle(16, 502, 60, 22), null),
            new Node("character.stat.DisplayStats.MaxAC", "character", "character", "stat", "DisplayStats[Stat.MaxAC]", new Rectangle(60, 420, 76, 20), null),
            new Node("character.stat.DisplayStats.MaxMR", "character", "character", "stat", "DisplayStats[Stat.MaxMR]", new Rectangle(186, 420, 76, 20), null),
            new Node("character.stat.DisplayStats.MaxDC", "character", "character", "stat", "DisplayStats[Stat.MaxDC]", new Rectangle(60, 396, 76, 20), null),
            new Node("character.stat.DisplayStats.MaxMC", "character", "character", "stat", "DisplayStats[Stat.MaxMC]", new Rectangle(186, 396, 76, 20), null),
            new Node("character.stat.DisplayStats.MaxSC", "character", "character", "stat", "DisplayStats[Stat.MaxSC]", new Rectangle(312, 396, 76, 20), null),
            new Node("character.stat.DisplayStats.Accuracy", "character", "character", "stat", "DisplayStats[Stat.Accuracy]", new Rectangle(310, 242, 86, 20), null),
            new Node("character.stat.DisplayStats.Agility", "character", "character", "stat", "DisplayStats[Stat.Agility]", new Rectangle(310, 266, 86, 20), null),
            new Node("character.stat.DisplayStats.AttackSpeed", "character", "status", "stat", "DisplayStats[Stat.AttackSpeed]", new Rectangle(16, 94, 180, 28), null),
            new Node("character.stat.DisplayStats.Luck", "character", "character", "stat", "DisplayStats[Stat.Luck]", new Rectangle(310, 314, 86, 20), null),
            new Node("character.stat.DisplayStats.Comfort", "character", "character", "stat", "DisplayStats[Stat.Comfort]", new Rectangle(310, 290, 86, 20), null),
            new Node("character.stat.DisplayStats.LifeSteal", "character", "status", "stat", "DisplayStats[Stat.LifeSteal]", new Rectangle(16, 130, 180, 28), null),
            new Node("character.stat.DisplayStats.GoldRate", "character", "status", "stat", "DisplayStats[Stat.GoldRate]", new Rectangle(16, 166, 180, 28), null),
            new Node("character.stat.DisplayStats.CriticalChance", "character", "status", "stat", "DisplayStats[Stat.CriticalChance]", new Rectangle(16, 202, 180, 28), null),
            new Node("character.stat.DisplayStats.DropRate", "character", "status", "stat", "DisplayStats[Stat.DropRate]", new Rectangle(16, 238, 180, 28), null),
            new Node("character.stat.DisplayStats.PickUpRadius", "character", "status", "stat", "DisplayStats[Stat.PickUpRadius]", new Rectangle(16, 274, 180, 28), null),
            new Node("character.stat.DisplayStats.ExperienceRate", "character", "status", "stat", "DisplayStats[Stat.ExperienceRate]", new Rectangle(16, 310, 180, 28), null),
            new Node("character.stat.DisplayStats.CriticalDamage", "character", "status", "stat", "DisplayStats[Stat.CriticalDamage]", new Rectangle(210, 94, 180, 28), null),
            new Node("character.stat.DisplayStats.Rebirth", "character", "status", "stat", "DisplayStats[Stat.Rebirth]", new Rectangle(210, 130, 180, 28), null),
            new Node("character.stat.DisplayStats.BlockChance", "character", "status", "stat", "DisplayStats[Stat.BlockChance]", new Rectangle(210, 166, 180, 28), null),
            new Node("character.stat.DisplayStats.EvasionChance", "character", "status", "stat", "DisplayStats[Stat.EvasionChance]", new Rectangle(210, 202, 180, 28), null),
            new Node("character.stat.AttackStats.FireAttack", "character", "character", "stat", "AttackStats[Stat.FireAttack]", new Rectangle(80, 450, 40, 22), null),
            new Node("character.stat.AttackStats.IceAttack", "character", "character", "stat", "AttackStats[Stat.IceAttack]", new Rectangle(124, 450, 40, 22), null),
            new Node("character.stat.AttackStats.LightningAttack", "character", "character", "stat", "AttackStats[Stat.LightningAttack]", new Rectangle(168, 450, 40, 22), null),
            new Node("character.stat.AttackStats.WindAttack", "character", "character", "stat", "AttackStats[Stat.WindAttack]", new Rectangle(212, 450, 40, 22), null),
            new Node("character.stat.AttackStats.HolyAttack", "character", "character", "stat", "AttackStats[Stat.HolyAttack]", new Rectangle(256, 450, 40, 22), null),
            new Node("character.stat.AttackStats.DarkAttack", "character", "character", "stat", "AttackStats[Stat.DarkAttack]", new Rectangle(300, 450, 40, 22), null),
            new Node("character.stat.AttackStats.PhantomAttack", "character", "character", "stat", "AttackStats[Stat.PhantomAttack]", new Rectangle(344, 450, 40, 22), null),
            new Node("character.stat.AdvantageStats.FireResistance", "character", "character", "stat", "AdvantageStats[Stat.FireResistance]", new Rectangle(80, 476, 40, 22), null),
            new Node("character.stat.AdvantageStats.IceResistance", "character", "character", "stat", "AdvantageStats[Stat.IceResistance]", new Rectangle(124, 476, 40, 22), null),
            new Node("character.stat.AdvantageStats.LightningResistance", "character", "character", "stat", "AdvantageStats[Stat.LightningResistance]", new Rectangle(168, 476, 40, 22), null),
            new Node("character.stat.AdvantageStats.WindResistance", "character", "character", "stat", "AdvantageStats[Stat.WindResistance]", new Rectangle(212, 476, 40, 22), null),
            new Node("character.stat.AdvantageStats.HolyResistance", "character", "character", "stat", "AdvantageStats[Stat.HolyResistance]", new Rectangle(256, 476, 40, 22), null),
            new Node("character.stat.AdvantageStats.DarkResistance", "character", "character", "stat", "AdvantageStats[Stat.DarkResistance]", new Rectangle(300, 476, 40, 22), null),
            new Node("character.stat.AdvantageStats.PhantomResistance", "character", "character", "stat", "AdvantageStats[Stat.PhantomResistance]", new Rectangle(344, 476, 40, 22), null),
            new Node("character.stat.AdvantageStats.PhysicalResistance", "character", "status", "stat", "AdvantageStats[Stat.PhysicalResistance]", new Rectangle(210, 238, 180, 28), null),
            new Node("character.stat.DisadvantageStats.FireResistance", "character", "character", "stat", "DisadvantageStats[Stat.FireResistance]", new Rectangle(80, 502, 40, 22), null),
            new Node("character.stat.DisadvantageStats.IceResistance", "character", "character", "stat", "DisadvantageStats[Stat.IceResistance]", new Rectangle(124, 502, 40, 22), null),
            new Node("character.stat.DisadvantageStats.LightningResistance", "character", "character", "stat", "DisadvantageStats[Stat.LightningResistance]", new Rectangle(168, 502, 40, 22), null),
            new Node("character.stat.DisadvantageStats.WindResistance", "character", "character", "stat", "DisadvantageStats[Stat.WindResistance]", new Rectangle(212, 502, 40, 22), null),
            new Node("character.stat.DisadvantageStats.HolyResistance", "character", "character", "stat", "DisadvantageStats[Stat.HolyResistance]", new Rectangle(256, 502, 40, 22), null),
            new Node("character.stat.DisadvantageStats.DarkResistance", "character", "character", "stat", "DisadvantageStats[Stat.DarkResistance]", new Rectangle(300, 502, 40, 22), null),
            new Node("character.stat.DisadvantageStats.PhantomResistance", "character", "character", "stat", "DisadvantageStats[Stat.PhantomResistance]", new Rectangle(344, 502, 40, 22), null),
            new Node("character.stat.DisadvantageStats.PhysicalResistance", "character", "status", "stat", "DisadvantageStats[Stat.PhysicalResistance]", new Rectangle(210, 274, 180, 28), null),
            new Node("character.stat.HermitDisplayStats.MaxAC", "character", "hermit", "stat", "HermitDisplayStats[Stat.MaxAC]", new Rectangle(16, 94, 154, 26), null),
            new Node("character.stat.HermitDisplayStats.MaxMR", "character", "hermit", "stat", "HermitDisplayStats[Stat.MaxMR]", new Rectangle(16, 128, 154, 26), null),
            new Node("character.stat.HermitDisplayStats.MaxDC", "character", "hermit", "stat", "HermitDisplayStats[Stat.MaxDC]", new Rectangle(16, 162, 154, 26), null),
            new Node("character.stat.HermitDisplayStats.MaxMC", "character", "hermit", "stat", "HermitDisplayStats[Stat.MaxMC]", new Rectangle(16, 196, 154, 26), null),
            new Node("character.stat.HermitDisplayStats.MaxSC", "character", "hermit", "stat", "HermitDisplayStats[Stat.MaxSC]", new Rectangle(16, 230, 154, 26), null),
            new Node("character.stat.HermitDisplayStats.Health", "character", "hermit", "stat", "HermitDisplayStats[Stat.Health]", new Rectangle(16, 264, 154, 26), null),
            new Node("character.stat.HermitDisplayStats.Mana", "character", "hermit", "stat", "HermitDisplayStats[Stat.Mana]", new Rectangle(16, 298, 154, 26), null),
            new Node("character.stat.HermitAttackStats.FireAttack", "character", "hermit", "stat", "HermitAttackStats[Stat.FireAttack]", new Rectangle(224, 94, 170, 26), null),
            new Node("character.stat.HermitAttackStats.IceAttack", "character", "hermit", "stat", "HermitAttackStats[Stat.IceAttack]", new Rectangle(224, 128, 170, 26), null),
            new Node("character.stat.HermitAttackStats.LightningAttack", "character", "hermit", "stat", "HermitAttackStats[Stat.LightningAttack]", new Rectangle(224, 162, 170, 26), null),
            new Node("character.stat.HermitAttackStats.WindAttack", "character", "hermit", "stat", "HermitAttackStats[Stat.WindAttack]", new Rectangle(224, 196, 170, 26), null),
            new Node("character.stat.HermitAttackStats.HolyAttack", "character", "hermit", "stat", "HermitAttackStats[Stat.HolyAttack]", new Rectangle(224, 230, 170, 26), null),
            new Node("character.stat.HermitAttackStats.DarkAttack", "character", "hermit", "stat", "HermitAttackStats[Stat.DarkAttack]", new Rectangle(224, 264, 170, 26), null),
            new Node("character.stat.HermitAttackStats.PhantomAttack", "character", "hermit", "stat", "HermitAttackStats[Stat.PhantomAttack]", new Rectangle(224, 298, 170, 26), null),
            new Node("character.immediate.new.level", "character", "character", "stat", "", new Rectangle(310, 98, 86, 20), null),
            new Node("character.immediate.new.experience", "character", "character", "stat", "", new Rectangle(310, 122, 86, 20), null),
            new Node("character.immediate.new.health", "character", "character", "stat", "", new Rectangle(310, 146, 86, 20), null),
            new Node("character.immediate.new.mana", "character", "character", "stat", "", new Rectangle(310, 170, 86, 20), null),
            new Node("character.control.CharacterNameLabel", "character", "character", "stat", "CharacterNameLabel", new Rectangle(22, 10, 348, 22), null),
            new Node("character.control.GuildNameLabel", "character", "character", "stat", "GuildNameLabel", new Rectangle(16, 66, 224, 22), null),
            new Node("character.control.GuildRankLabel", "character", "character", "stat", "GuildRankLabel", new Rectangle(254, 66, 142, 22), null),
            new Node("character.control.WearWeightLabel", "character", "character", "stat", "WearWeightLabel", new Rectangle(310, 218, 86, 20), null),
            new Node("character.control.HandWeightLabel", "character", "character", "stat", "HandWeightLabel", new Rectangle(310, 194, 86, 20), null),
            new Node("character.control.RemainingLabel", "character", "hermit", "stat", "RemainingLabel", new Rectangle(16, 370, 180, 24), null),
            new Node("character.control.MarriageIcon", "character", "character", "stat", "MarriageIcon", new Rectangle(370, 12, 14, 16), null),
            new Node("character.control.ShowHelmetBox", "character", "character", "control", "ShowHelmetBox", new Rectangle(254, 340, 142, 22), new Rectangle(254, 340, 142, 22)),
            new Node("character.action.hermit.MaxAC", "character", "hermit", "control", "", new Rectangle(174, 94, 24, 26), new Rectangle(174, 94, 24, 26)),
            new Node("character.action.hermit.MaxMR", "character", "hermit", "control", "", new Rectangle(174, 128, 24, 26), new Rectangle(174, 128, 24, 26)),
            new Node("character.action.hermit.Health", "character", "hermit", "control", "", new Rectangle(174, 264, 24, 26), new Rectangle(174, 264, 24, 26)),
            new Node("character.action.hermit.Mana", "character", "hermit", "control", "", new Rectangle(174, 298, 24, 26), new Rectangle(174, 298, 24, 26)),
            new Node("character.action.hermit.MaxDC", "character", "hermit", "control", "", new Rectangle(174, 162, 24, 26), new Rectangle(174, 162, 24, 26)),
            new Node("character.action.hermit.MaxMC", "character", "hermit", "control", "", new Rectangle(174, 196, 24, 26), new Rectangle(174, 196, 24, 26)),
            new Node("character.action.hermit.MaxSC", "character", "hermit", "control", "", new Rectangle(174, 230, 24, 26), new Rectangle(174, 230, 24, 26)),
            new Node("character.action.hermit.WeaponElement", "character", "hermit", "control", "", new Rectangle(174, 332, 24, 26), new Rectangle(174, 332, 24, 26)),
            new Node("character.hermit.confirmation", "character", "hermit", "control", "existing local DXCheckBox check in CharacterDialog constructor", new Rectangle(16, 402, 180, 24), new Rectangle(16, 402, 180, 24)),
            new Node("inventory.close", "inventory", "all", "control", "CloseButton", new Rectangle(250, 10, 18, 18), new Rectangle(250, 10, 18, 18)),
            new Node("inventory.cell.0", "inventory", "all", "control", "Grid.Grid[0]", new Rectangle(15, 49, 36, 36), new Rectangle(15, 49, 35, 35)),
            new Node("inventory.cell.1", "inventory", "all", "control", "Grid.Grid[1]", new Rectangle(50, 49, 36, 36), new Rectangle(50, 49, 35, 35)),
            new Node("inventory.cell.2", "inventory", "all", "control", "Grid.Grid[2]", new Rectangle(85, 49, 36, 36), new Rectangle(85, 49, 35, 35)),
            new Node("inventory.cell.3", "inventory", "all", "control", "Grid.Grid[3]", new Rectangle(120, 49, 36, 36), new Rectangle(120, 49, 35, 35)),
            new Node("inventory.cell.4", "inventory", "all", "control", "Grid.Grid[4]", new Rectangle(155, 49, 36, 36), new Rectangle(155, 49, 35, 35)),
            new Node("inventory.cell.5", "inventory", "all", "control", "Grid.Grid[5]", new Rectangle(190, 49, 36, 36), new Rectangle(190, 49, 35, 35)),
            new Node("inventory.cell.6", "inventory", "all", "control", "Grid.Grid[6]", new Rectangle(225, 49, 36, 36), new Rectangle(225, 49, 36, 35)),
            new Node("inventory.cell.7", "inventory", "all", "control", "Grid.Grid[7]", new Rectangle(15, 84, 36, 36), new Rectangle(15, 84, 35, 35)),
            new Node("inventory.cell.8", "inventory", "all", "control", "Grid.Grid[8]", new Rectangle(50, 84, 36, 36), new Rectangle(50, 84, 35, 35)),
            new Node("inventory.cell.9", "inventory", "all", "control", "Grid.Grid[9]", new Rectangle(85, 84, 36, 36), new Rectangle(85, 84, 35, 35)),
            new Node("inventory.cell.10", "inventory", "all", "control", "Grid.Grid[10]", new Rectangle(120, 84, 36, 36), new Rectangle(120, 84, 35, 35)),
            new Node("inventory.cell.11", "inventory", "all", "control", "Grid.Grid[11]", new Rectangle(155, 84, 36, 36), new Rectangle(155, 84, 35, 35)),
            new Node("inventory.cell.12", "inventory", "all", "control", "Grid.Grid[12]", new Rectangle(190, 84, 36, 36), new Rectangle(190, 84, 35, 35)),
            new Node("inventory.cell.13", "inventory", "all", "control", "Grid.Grid[13]", new Rectangle(225, 84, 36, 36), new Rectangle(225, 84, 36, 35)),
            new Node("inventory.cell.14", "inventory", "all", "control", "Grid.Grid[14]", new Rectangle(15, 119, 36, 36), new Rectangle(15, 119, 35, 35)),
            new Node("inventory.cell.15", "inventory", "all", "control", "Grid.Grid[15]", new Rectangle(50, 119, 36, 36), new Rectangle(50, 119, 35, 35)),
            new Node("inventory.cell.16", "inventory", "all", "control", "Grid.Grid[16]", new Rectangle(85, 119, 36, 36), new Rectangle(85, 119, 35, 35)),
            new Node("inventory.cell.17", "inventory", "all", "control", "Grid.Grid[17]", new Rectangle(120, 119, 36, 36), new Rectangle(120, 119, 35, 35)),
            new Node("inventory.cell.18", "inventory", "all", "control", "Grid.Grid[18]", new Rectangle(155, 119, 36, 36), new Rectangle(155, 119, 35, 35)),
            new Node("inventory.cell.19", "inventory", "all", "control", "Grid.Grid[19]", new Rectangle(190, 119, 36, 36), new Rectangle(190, 119, 35, 35)),
            new Node("inventory.cell.20", "inventory", "all", "control", "Grid.Grid[20]", new Rectangle(225, 119, 36, 36), new Rectangle(225, 119, 36, 35)),
            new Node("inventory.cell.21", "inventory", "all", "control", "Grid.Grid[21]", new Rectangle(15, 154, 36, 36), new Rectangle(15, 154, 35, 35)),
            new Node("inventory.cell.22", "inventory", "all", "control", "Grid.Grid[22]", new Rectangle(50, 154, 36, 36), new Rectangle(50, 154, 35, 35)),
            new Node("inventory.cell.23", "inventory", "all", "control", "Grid.Grid[23]", new Rectangle(85, 154, 36, 36), new Rectangle(85, 154, 35, 35)),
            new Node("inventory.cell.24", "inventory", "all", "control", "Grid.Grid[24]", new Rectangle(120, 154, 36, 36), new Rectangle(120, 154, 35, 35)),
            new Node("inventory.cell.25", "inventory", "all", "control", "Grid.Grid[25]", new Rectangle(155, 154, 36, 36), new Rectangle(155, 154, 35, 35)),
            new Node("inventory.cell.26", "inventory", "all", "control", "Grid.Grid[26]", new Rectangle(190, 154, 36, 36), new Rectangle(190, 154, 35, 35)),
            new Node("inventory.cell.27", "inventory", "all", "control", "Grid.Grid[27]", new Rectangle(225, 154, 36, 36), new Rectangle(225, 154, 36, 35)),
            new Node("inventory.cell.28", "inventory", "all", "control", "Grid.Grid[28]", new Rectangle(15, 189, 36, 36), new Rectangle(15, 189, 35, 35)),
            new Node("inventory.cell.29", "inventory", "all", "control", "Grid.Grid[29]", new Rectangle(50, 189, 36, 36), new Rectangle(50, 189, 35, 35)),
            new Node("inventory.cell.30", "inventory", "all", "control", "Grid.Grid[30]", new Rectangle(85, 189, 36, 36), new Rectangle(85, 189, 35, 35)),
            new Node("inventory.cell.31", "inventory", "all", "control", "Grid.Grid[31]", new Rectangle(120, 189, 36, 36), new Rectangle(120, 189, 35, 35)),
            new Node("inventory.cell.32", "inventory", "all", "control", "Grid.Grid[32]", new Rectangle(155, 189, 36, 36), new Rectangle(155, 189, 35, 35)),
            new Node("inventory.cell.33", "inventory", "all", "control", "Grid.Grid[33]", new Rectangle(190, 189, 36, 36), new Rectangle(190, 189, 35, 35)),
            new Node("inventory.cell.34", "inventory", "all", "control", "Grid.Grid[34]", new Rectangle(225, 189, 36, 36), new Rectangle(225, 189, 36, 35)),
            new Node("inventory.cell.35", "inventory", "all", "control", "Grid.Grid[35]", new Rectangle(15, 224, 36, 36), new Rectangle(15, 224, 35, 35)),
            new Node("inventory.cell.36", "inventory", "all", "control", "Grid.Grid[36]", new Rectangle(50, 224, 36, 36), new Rectangle(50, 224, 35, 35)),
            new Node("inventory.cell.37", "inventory", "all", "control", "Grid.Grid[37]", new Rectangle(85, 224, 36, 36), new Rectangle(85, 224, 35, 35)),
            new Node("inventory.cell.38", "inventory", "all", "control", "Grid.Grid[38]", new Rectangle(120, 224, 36, 36), new Rectangle(120, 224, 35, 35)),
            new Node("inventory.cell.39", "inventory", "all", "control", "Grid.Grid[39]", new Rectangle(155, 224, 36, 36), new Rectangle(155, 224, 35, 35)),
            new Node("inventory.cell.40", "inventory", "all", "control", "Grid.Grid[40]", new Rectangle(190, 224, 36, 36), new Rectangle(190, 224, 35, 35)),
            new Node("inventory.cell.41", "inventory", "all", "control", "Grid.Grid[41]", new Rectangle(225, 224, 36, 36), new Rectangle(225, 224, 36, 35)),
            new Node("inventory.cell.42", "inventory", "all", "control", "Grid.Grid[42]", new Rectangle(15, 259, 36, 36), new Rectangle(15, 259, 35, 36)),
            new Node("inventory.cell.43", "inventory", "all", "control", "Grid.Grid[43]", new Rectangle(50, 259, 36, 36), new Rectangle(50, 259, 35, 36)),
            new Node("inventory.cell.44", "inventory", "all", "control", "Grid.Grid[44]", new Rectangle(85, 259, 36, 36), new Rectangle(85, 259, 35, 36)),
            new Node("inventory.cell.45", "inventory", "all", "control", "Grid.Grid[45]", new Rectangle(120, 259, 36, 36), new Rectangle(120, 259, 35, 36)),
            new Node("inventory.cell.46", "inventory", "all", "control", "Grid.Grid[46]", new Rectangle(155, 259, 36, 36), new Rectangle(155, 259, 35, 36)),
            new Node("inventory.cell.47", "inventory", "all", "control", "Grid.Grid[47]", new Rectangle(190, 259, 36, 36), new Rectangle(190, 259, 35, 36)),
            new Node("inventory.cell.48", "inventory", "all", "control", "Grid.Grid[48]", new Rectangle(225, 259, 36, 36), new Rectangle(225, 259, 36, 36)),
            new Node("inventory.weight", "inventory", "all", "stat", "WeightLabel", new Rectangle(70, 304, 190, 20), null),
            new Node("inventory.gold", "inventory", "all", "control", "GoldLabel", new Rectangle(70, 328, 190, 20), new Rectangle(70, 328, 190, 20)),
            new Node("inventory.action.sell", "inventory", "all", "control", "", new Rectangle(16, 354, 78, 24), new Rectangle(16, 354, 78, 24)),
            new Node("inventory.action.fragment", "inventory", "all", "control", "", new Rectangle(95, 354, 78, 24), new Rectangle(95, 354, 78, 24)),
            new Node("inventory.action.drops", "inventory", "all", "control", "", new Rectangle(174, 354, 78, 24), new Rectangle(174, 354, 78, 24)),
            new Node("quickbar.cell.0", "quickbar", "all", "control", "Grid.Grid[0]", new Rectangle(60, 4, 36, 36), new Rectangle(60, 4, 35, 36)),
            new Node("quickbar.cell.1", "quickbar", "all", "control", "Grid.Grid[1]", new Rectangle(95, 4, 36, 36), new Rectangle(95, 4, 35, 36)),
            new Node("quickbar.cell.2", "quickbar", "all", "control", "Grid.Grid[2]", new Rectangle(130, 4, 36, 36), new Rectangle(130, 4, 35, 36)),
            new Node("quickbar.cell.3", "quickbar", "all", "control", "Grid.Grid[3]", new Rectangle(165, 4, 36, 36), new Rectangle(165, 4, 35, 36)),
            new Node("quickbar.cell.4", "quickbar", "all", "control", "Grid.Grid[4]", new Rectangle(200, 4, 36, 36), new Rectangle(200, 4, 35, 36)),
            new Node("quickbar.cell.5", "quickbar", "all", "control", "Grid.Grid[5]", new Rectangle(235, 4, 36, 36), new Rectangle(235, 4, 35, 36)),
            new Node("quickbar.cell.6", "quickbar", "all", "control", "Grid.Grid[6]", new Rectangle(270, 4, 36, 36), new Rectangle(270, 4, 35, 36)),
            new Node("quickbar.cell.7", "quickbar", "all", "control", "Grid.Grid[7]", new Rectangle(305, 4, 36, 36), new Rectangle(305, 4, 35, 36)),
            new Node("quickbar.cell.8", "quickbar", "all", "control", "Grid.Grid[8]", new Rectangle(340, 4, 36, 36), new Rectangle(340, 4, 35, 36)),
            new Node("quickbar.cell.9", "quickbar", "all", "control", "Grid.Grid[9]", new Rectangle(375, 4, 36, 36), new Rectangle(375, 4, 36, 36)),
            new Node("minimap.viewport", "minimap", "all", "viewport", "Panel/Area", new Rectangle(11, 21, 150, 152), null),
            new Node("minimap.coordinates", "minimap", "all", "stat", "", new Rectangle(24, 177, 118, 16), null),
            new Node("minimap.collapse", "minimap", "all", "control", "", new Rectangle(146, 2, 20, 16), new Rectangle(146, 2, 20, 16)),
            new Node("minimap.footer.ornament", "minimap", "all", "decoration", "", new Rectangle(4, 177, 20, 16), null),
            new Node("chat.tabs", "chat", "all", "dynamic-tabs", "existing-ChatTab.TabButton", new Rectangle(4, 2, 566, 22), null),
            new Node("chat.history", "chat", "all", "viewport", "ChatTab.TextPanel", new Rectangle(4, 26, 548, 76), null),
            new Node("chat.scrollbar", "chat", "all", "control", "ChatTab.ScrollBar", new Rectangle(554, 26, 16, 76), new Rectangle(554, 26, 16, 76)),
            new Node("chatInput.mode", "chatInput", "all", "control", "ChatModeButton", new Rectangle(4, 1, 56, 20), new Rectangle(4, 1, 56, 20)),
            new Node("chatInput.text", "chatInput", "all", "control", "TextBox", new Rectangle(64, 1, 480, 20), new Rectangle(64, 1, 480, 20)),
            new Node("chatInput.options", "chatInput", "all", "control", "OptionsButton", new Rectangle(548, 1, 22, 20), new Rectangle(548, 1, 22, 20)),
            new Node("commands.CharacterButton", "commands", "all", "control", "CharacterButton", new Rectangle(85, 73, 58, 58), new Rectangle(85, 73, 58, 58)),
            new Node("commands.InventoryButton", "commands", "all", "control", "InventoryButton", new Rectangle(86, 38, 54, 32), new Rectangle(86, 38, 54, 32)),
            new Node("commands.SpellButton", "commands", "all", "control", "SpellButton", new Rectangle(40, 73, 42, 50), new Rectangle(40, 73, 42, 50)),
            new Node("commands.QuestButton", "commands", "all", "control", "QuestButton", new Rectangle(146, 73, 42, 50), new Rectangle(146, 73, 42, 50)),
            new Node("commands.MailButton", "commands", "all", "control", "MailButton", new Rectangle(40, 134, 42, 50), new Rectangle(40, 134, 42, 50)),
            new Node("commands.GroupButton", "commands", "all", "control", "GroupButton", new Rectangle(146, 134, 42, 50), new Rectangle(146, 134, 42, 50)),
            new Node("commands.ConfigButton", "commands", "all", "control", "ConfigButton", new Rectangle(85, 134, 54, 32), new Rectangle(85, 134, 54, 32)),
            new Node("commands.CashShopButton", "commands", "all", "control", "CashShopButton", new Rectangle(181, 4, 40, 40), new Rectangle(181, 4, 40, 40)),
            new Node("commands.BeltButton", "commands", "all", "control", "BeltButton", new Rectangle(5, 75, 31, 24), new Rectangle(5, 75, 31, 24)),
            new Node("commands.attackMode", "commands", "all", "stat", "AttackModeLabel", new Rectangle(4, 4, 172, 16), null),
            new Node("commands.petMode", "commands", "all", "stat", "PetModeLabel", new Rectangle(4, 21, 172, 16), null),
            new Node("orb.shell", "orb", "all", "decoration", "", new Rectangle(0, 0, 225, 202), null),
            new Node("orb.fill.health", "orb", "all", "fill", "", new Rectangle(43, 75, 53, 101), null),
            new Node("orb.fill.mana", "orb", "all", "fill", "", new Rectangle(96, 75, 48, 101), null),
            new Node("orb.ClassLabel", "orb", "all", "stat", "ClassLabel", new Rectangle(4, 4, 84, 16), null),
            new Node("orb.LevelLabel", "orb", "all", "stat", "LevelLabel", new Rectangle(90, 4, 84, 16), null),
            new Node("orb.HealthLabel", "orb", "all", "stat", "HealthLabel", new Rectangle(20, 150, 70, 18), null),
            new Node("orb.ManaLabel", "orb", "all", "stat", "ManaLabel", new Rectangle(96, 150, 70, 18), null),
            new Node("readouts.ACLabel", "readouts", "all", "stat", "ACLabel", new Rectangle(0, 0, 80, 16), null),
            new Node("readouts.MRLabel", "readouts", "all", "stat", "MRLabel", new Rectangle(82, 0, 80, 16), null),
            new Node("readouts.DCLabel", "readouts", "all", "stat", "DCLabel", new Rectangle(164, 0, 80, 16), null),
            new Node("readouts.MCLabel", "readouts", "all", "stat", "MCLabel", new Rectangle(246, 0, 80, 16), null),
            new Node("readouts.SCLabel", "readouts", "all", "stat", "SCLabel", new Rectangle(328, 0, 80, 16), null),
            new Node("readouts.AccuracyLabel", "readouts", "all", "stat", "AccuracyLabel", new Rectangle(410, 0, 80, 16), null),
            new Node("readouts.AgilityLabel", "readouts", "all", "stat", "AgilityLabel", new Rectangle(492, 0, 80, 16), null),
            new Node("experience.fill", "experience", "all", "fill", "ExperienceBar", new Rectangle(0, 0, 574, 8), null),
            new Node("experience.label", "experience", "all", "stat", "ExperienceLabel", new Rectangle(0, 0, 574, 8), null),
            new Node("commands.alert.NewMailIcon", "commands", "all", "decoration", "NewMailIcon", new Rectangle(40, 134, 14, 14), null),
            new Node("commands.alert.CompletedQuestIcon", "commands", "all", "decoration", "CompletedQuestIcon", new Rectangle(146, 73, 14, 14), null),
            new Node("commands.alert.AvailableQuestIcon", "commands", "all", "decoration", "AvailableQuestIcon", new Rectangle(146, 73, 14, 14), null),
            new Node("character.hermit.weaponElement.caption", "character", "hermit", "caption", "", new Rectangle(16, 332, 154, 26), null),
        });
        public static bool TryGetPlacement(int width, int height, string owner, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (width == 1024 && height == 768)
            {
                switch (owner)
                {
                    case "character": bounds = new Rectangle(8, 4, 412, 562); return true;
                    case "inventory": bounds = new Rectangle(740, 56, 276, 412); return true;
                    case "orb": bounds = new Rectangle(0, 566, 225, 202); return true;
                    case "commands": bounds = new Rectangle(799, 566, 225, 202); return true;
                    case "quickbar": bounds = new Rectangle(282, 570, 459, 44); return true;
                    case "readouts": bounds = new Rectangle(225, 614, 574, 16); return true;
                    case "chat": bounds = new Rectangle(225, 630, 574, 106); return true;
                    case "chatInput": bounds = new Rectangle(225, 738, 574, 22); return true;
                    case "experience": bounds = new Rectangle(225, 760, 574, 8); return true;
                    case "minimap": bounds = new Rectangle(858, 0, 166, 198); return true;
                }
                return false;
            }
            if (width == 1366 && height == 768)
            {
                switch (owner)
                {
                    case "character": bounds = new Rectangle(8, 4, 412, 562); return true;
                    case "inventory": bounds = new Rectangle(1082, 56, 276, 412); return true;
                    case "orb": bounds = new Rectangle(171, 566, 225, 202); return true;
                    case "commands": bounds = new Rectangle(970, 566, 225, 202); return true;
                    case "quickbar": bounds = new Rectangle(453, 570, 459, 44); return true;
                    case "readouts": bounds = new Rectangle(396, 614, 574, 16); return true;
                    case "chat": bounds = new Rectangle(396, 630, 574, 106); return true;
                    case "chatInput": bounds = new Rectangle(396, 738, 574, 22); return true;
                    case "experience": bounds = new Rectangle(396, 760, 574, 8); return true;
                    case "minimap": bounds = new Rectangle(1200, 0, 166, 198); return true;
                }
                return false;
            }
            if (width == 1280 && height == 800)
            {
                switch (owner)
                {
                    case "character": bounds = new Rectangle(8, 4, 412, 562); return true;
                    case "inventory": bounds = new Rectangle(996, 56, 276, 412); return true;
                    case "orb": bounds = new Rectangle(128, 598, 225, 202); return true;
                    case "commands": bounds = new Rectangle(927, 598, 225, 202); return true;
                    case "quickbar": bounds = new Rectangle(410, 602, 459, 44); return true;
                    case "readouts": bounds = new Rectangle(353, 646, 574, 16); return true;
                    case "chat": bounds = new Rectangle(353, 662, 574, 106); return true;
                    case "chatInput": bounds = new Rectangle(353, 770, 574, 22); return true;
                    case "experience": bounds = new Rectangle(353, 792, 574, 8); return true;
                    case "minimap": bounds = new Rectangle(1114, 0, 166, 198); return true;
                }
                return false;
            }
            if (width == 1440 && height == 900)
            {
                switch (owner)
                {
                    case "character": bounds = new Rectangle(8, 4, 412, 562); return true;
                    case "inventory": bounds = new Rectangle(1156, 56, 276, 412); return true;
                    case "orb": bounds = new Rectangle(208, 698, 225, 202); return true;
                    case "commands": bounds = new Rectangle(1007, 698, 225, 202); return true;
                    case "quickbar": bounds = new Rectangle(490, 702, 459, 44); return true;
                    case "readouts": bounds = new Rectangle(433, 746, 574, 16); return true;
                    case "chat": bounds = new Rectangle(433, 762, 574, 106); return true;
                    case "chatInput": bounds = new Rectangle(433, 870, 574, 22); return true;
                    case "experience": bounds = new Rectangle(433, 892, 574, 8); return true;
                    case "minimap": bounds = new Rectangle(1274, 0, 166, 198); return true;
                }
                return false;
            }
            if (width == 1600 && height == 900)
            {
                switch (owner)
                {
                    case "character": bounds = new Rectangle(8, 4, 412, 562); return true;
                    case "inventory": bounds = new Rectangle(1316, 56, 276, 412); return true;
                    case "orb": bounds = new Rectangle(288, 698, 225, 202); return true;
                    case "commands": bounds = new Rectangle(1087, 698, 225, 202); return true;
                    case "quickbar": bounds = new Rectangle(570, 702, 459, 44); return true;
                    case "readouts": bounds = new Rectangle(513, 746, 574, 16); return true;
                    case "chat": bounds = new Rectangle(513, 762, 574, 106); return true;
                    case "chatInput": bounds = new Rectangle(513, 870, 574, 22); return true;
                    case "experience": bounds = new Rectangle(513, 892, 574, 8); return true;
                    case "minimap": bounds = new Rectangle(1434, 0, 166, 198); return true;
                }
                return false;
            }
            if (width == 1920 && height == 1080)
            {
                switch (owner)
                {
                    case "character": bounds = new Rectangle(8, 4, 412, 562); return true;
                    case "inventory": bounds = new Rectangle(1636, 56, 276, 412); return true;
                    case "orb": bounds = new Rectangle(448, 878, 225, 202); return true;
                    case "commands": bounds = new Rectangle(1247, 878, 225, 202); return true;
                    case "quickbar": bounds = new Rectangle(730, 882, 459, 44); return true;
                    case "readouts": bounds = new Rectangle(673, 926, 574, 16); return true;
                    case "chat": bounds = new Rectangle(673, 942, 574, 106); return true;
                    case "chatInput": bounds = new Rectangle(673, 1050, 574, 22); return true;
                    case "experience": bounds = new Rectangle(673, 1072, 574, 8); return true;
                    case "minimap": bounds = new Rectangle(1754, 0, 166, 198); return true;
                }
                return false;
            }
            return false;
        }
    }
}
