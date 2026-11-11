using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Library;
using Library.Network;
using Library.SystemModels;
using MirDB;
using Server.DBModels;
using Server.Envir;
using Server.Models;
using C = Library.Network.ClientPackets;
using S = Library.Network.ServerPackets;

internal static class SharedEquipmentSlotTests
{
    static int checks;
    static Session session;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static SConnection Connection()
    {
        var c = (SConnection)FormatterServices.GetUninitializedObject(typeof(SConnection));
        c.Connected = true; c.SendList = new ConcurrentQueue<Packet>(); c.Observers = new List<SConnection>();
        return c;
    }
    static CharacterInfo Character()
    {
        var account = session.GetCollection<AccountInfo>().CreateNewObject();
        var c = session.GetCollection<CharacterInfo>().CreateNewObject();
        c.Account = account; c.CharacterName = "isolated-equipment-test"; c.Level = 1;
        c.Gender = MirGender.Male; c.Class = MirClass.Warrior;
        return c;
    }
    static PlayerObject Player(CharacterInfo c = null)
    {
        var p = new PlayerObject(c ?? Character(), Connection());
        p.Stats[Stat.HandWeight] = p.Stats[Stat.WearWeight] = p.Stats[Stat.BagWeight] = 100;
        return p;
    }
    static UserItem Item(ItemType type, int weight = 2)
    {
        var info = session.GetCollection<ItemInfo>().CreateNewObject();
        info.ItemType = type; info.ItemName = "test-" + type; info.Weight = weight;
        info.RequiredClass = RequiredClass.Warrior; info.RequiredGender = RequiredGender.Male;
        info.Durability = 100; info.Stats = new Stats(); info.StackSize = 1;
        var item = session.GetCollection<UserItem>().CreateNewObject();
        item.Info = info; item.Count = 1; item.CurrentDurability = 80;
        return item;
    }
    static void Equip(CharacterInfo c, UserItem item, EquipmentSlot slot)
    { item.Slot = Globals.EquipmentOffSet + (int)slot; item.Character = c; }
    static void Normalize(PlayerObject p)
    { typeof(PlayerObject).GetMethod("NormalizeSharedEmblemSlot", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(p, null); }
    static void Rules()
    {
        foreach (ItemType type in Enum.GetValues(typeof(ItemType)))
        {
            Check(!Functions.CorrectSlot(type, EquipmentSlot.Torch), "retired index accepts " + type);
            Check(Functions.CorrectSlot(type, EquipmentSlot.Emblem) == (type == ItemType.Emblem || type == ItemType.Torch), "shared slot accepts wrong type " + type);
        }
        Check(Functions.CorrectSlot(ItemType.Poison, EquipmentSlot.Poison), "poison lost its slot");
        Check(!Functions.CorrectSlot(ItemType.Poison, EquipmentSlot.Amulet), "poison merged into amulet");
        var p = Player(); var medal = Item(ItemType.Emblem, 30); var torch = Item(ItemType.Torch, 4);
        p.Equipment[(int)EquipmentSlot.Emblem] = medal; p.WearWeight = 30; p.HandWeight = 99;
        Check(!p.CanWearItem(torch, EquipmentSlot.Emblem), "medal weight incorrectly subtracted from hand weight");
        p.HandWeight = 96;
        Check(p.CanWearItem(torch, EquipmentSlot.Emblem), "exact hand capacity rejected");
        p.Equipment[(int)EquipmentSlot.Emblem] = torch; p.HandWeight = 4; p.WearWeight = 99;
        Check(!p.CanWearItem(medal, EquipmentSlot.Emblem), "torch weight incorrectly subtracted from wear weight");
        p.WearWeight = 70;
        Check(p.CanWearItem(medal, EquipmentSlot.Emblem), "exact wear capacity rejected");
        var stacked = Item(ItemType.Poison, 2); stacked.Count = 10; stacked.Info.StackSize = 99;
        p.Equipment[(int)EquipmentSlot.Poison] = stacked; p.WearWeight = 100;
        Check(p.CanWearItem(stacked, EquipmentSlot.Poison), "replacement did not subtract full stack weight");
        Check(!p.CanWearItem(torch, EquipmentSlot.Torch), "server allows second torch slot");
    }
    static void Migration()
    {
        foreach (bool occupied in new[] { false, true })
        foreach (bool full in new[] { false, true })
        {
            var c = Character(); var torch = Item(ItemType.Torch); var medal = occupied ? Item(ItemType.Emblem) : null;
            torch.Flags = UserItemFlags.Bound; torch.Stats[Stat.MaxDC] = 7; torch.CurrentDurability = 53;
            Equip(c, torch, EquipmentSlot.Torch); if (medal != null) Equip(c, medal, EquipmentSlot.Emblem);
            if (full) for (int i = 0; i < Globals.InventorySize; i++) { var filler = Item(ItemType.Consumable); filler.Character = c; filler.Slot = i; }
            var p = Player(c);
            Check(p.Equipment[(int)EquipmentSlot.Torch] == null, "legacy slot retained item");
            if (!occupied)
                Check(ReferenceEquals(p.Equipment[(int)EquipmentSlot.Emblem], torch) && torch.Slot == Globals.EquipmentOffSet + (int)EquipmentSlot.Emblem, "single torch migration failed");
            else
            {
                Check(ReferenceEquals(p.Equipment[(int)EquipmentSlot.Emblem], medal), "medal was displaced");
                if (full) Check(torch.Mail != null && torch.Character == null && torch.Mail.Account == c.Account && torch.Mail.HasItem && torch.Mail.Items.Contains(torch), "full bag lost torch or mail association");
                else Check(ReferenceEquals(p.Inventory[0], torch) && torch.Character == c && torch.Slot == 0, "bag fallback failed");
            }
            Check(torch.CurrentDurability == 53 && torch.Flags == UserItemFlags.Bound && torch.Stats[Stat.MaxDC] == 7 && torch.Count == 1, "migration changed item metadata");
            int mails = SEnvir.MailInfoList.Count; var mail = torch.Mail; int index = torch.Index;
            Normalize(p);
            var again = Player(c);
            Check(torch.Index == index && torch.Mail == mail && SEnvir.MailInfoList.Count == mails && again.Equipment[(int)EquipmentSlot.Torch] == null, "migration not idempotent on relog");
        }
    }
    static S.ItemMove Move(PlayerObject p, int to)
    {
        p.Connection.SendList = new ConcurrentQueue<Packet>();
        p.ItemMove(new C.ItemMove { FromGrid = GridType.Inventory, FromSlot = 0, ToGrid = GridType.Equipment, ToSlot = to });
        return p.Connection.SendList.OfType<S.ItemMove>().Single();
    }
    static void SwapAndBurn()
    {
        var p = Player(); var medal = Item(ItemType.Emblem, 7); var torch = Item(ItemType.Torch, 4);
        medal.Stats[Stat.MaxDC] = 10; torch.Stats[Stat.MaxDC] = 3;
        Equip(p.Character, medal, EquipmentSlot.Emblem); p.Equipment[(int)EquipmentSlot.Emblem] = medal;
        torch.Character = p.Character; torch.Slot = 0; p.Inventory[0] = torch; p.RefreshStats();
        Check(!Move(p, (int)EquipmentSlot.Torch).Success && p.Inventory[0] == torch, "obsolete target accepted move");
        Check(Move(p, (int)EquipmentSlot.Emblem).Success && p.Equipment[(int)EquipmentSlot.Emblem] == torch && p.Inventory[0] == medal, "medal to torch replacement lost identity");
        Check(p.HandWeight == 4 && p.WearWeight == 0 && p.Stats[Stat.MaxDC] == 3, "replacement stats or weight counted both items");
        p.Connection.SendList = new ConcurrentQueue<Packet>(); p.TorchTime = DateTime.MinValue;
        Config.TorchRate = 2; int durability = torch.CurrentDurability;
        p.ProcessTorch();
        Check(torch.CurrentDurability == durability - 2, "shared torch did not burn");
        Check(p.Connection.SendList.OfType<S.ItemDurability>().Single().Slot == (int)EquipmentSlot.Emblem, "torch update sent obsolete slot");
        p.ProcessTorch(); Check(torch.CurrentDurability == durability - 2, "burn ignored ten second interval");
        p.InSafeZone = true; p.TorchTime = DateTime.MinValue; p.ProcessTorch();
        Check(torch.CurrentDurability == durability - 2, "torch burns in safe zone"); p.InSafeZone = false;
        Check(Move(p, (int)EquipmentSlot.Emblem).Success && p.Inventory[0] == torch && p.Equipment[(int)EquipmentSlot.Emblem] == medal, "torch to medal replacement lost identity");
        Check(p.HandWeight == 0 && p.WearWeight == 7 && p.Stats[Stat.MaxDC] == 10, "medal replacement stats or weight incorrect");
        durability = medal.CurrentDurability; p.TorchTime = DateTime.MinValue; p.ProcessTorch();
        Check(medal.CurrentDurability == durability, "medal consumed as a torch");
        Move(p, (int)EquipmentSlot.Emblem); torch.CurrentDurability = 1; p.TorchTime = DateTime.MinValue; p.Connection.SendList = new ConcurrentQueue<Packet>(); p.ProcessTorch();
        Check(p.Equipment[(int)EquipmentSlot.Emblem] == null && !p.Character.Items.Contains(torch), "exhausted torch retained equipment or owner");
        Check(p.Connection.SendList.OfType<S.ItemChanged>().Single().Link.Slot == (int)EquipmentSlot.Emblem && p.HandWeight == 0 && p.Stats[Stat.MaxDC] == 0, "exhaustion update or stats incorrect");
    }
    static int Main()
    {
        try
        {
            session = new Session(SessionMode.Users, @".\IsolatedData\") { BackUp = false };
            var collections = (IDictionary)typeof(Session).GetField("Collections", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(session);
            foreach (var type in typeof(PlayerObject).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(DBObject))))
                if (!collections.Contains(type)) collections.Add(type, Activator.CreateInstance(typeof(DBCollection<>).MakeGenericType(type), session));
            SEnvir.Now = new DateTime(2026, 11, 10, 12, 0, 0); SEnvir.Random = new Random(1);
            SEnvir.BuffInfoList = session.GetCollection<BuffInfo>(); SEnvir.UserItemStatsList = session.GetCollection<UserItemStat>();
            SEnvir.MailInfoList = session.GetCollection<MailInfo>(); SEnvir.BaseStatList = session.GetCollection<BaseStat>();
            var stat = SEnvir.BaseStatList.CreateNewObject(); stat.Class = MirClass.Warrior; stat.Level = 1; stat.HandWeight = stat.WearWeight = stat.BagWeight = 100;
            Rules(); Migration(); SwapAndBurn();
            Console.WriteLine("Shared equipment: " + checks + " checks passed; real server ItemMove, replacement stats/weight, retired-slot rejection, 10-second torch wear/expiry, safe zone, medal exclusion, four migration/relogin cases including full-bag mail. Isolated MirDB objects and socket-free packet queues; no live server data.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}