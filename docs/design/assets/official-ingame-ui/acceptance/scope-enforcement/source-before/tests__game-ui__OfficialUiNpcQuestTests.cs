using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Client;
using Client.Controls;
using Client.Envir;
using Client.Scenes;
using Client.Scenes.Views;
using Library;
using Library.SystemModels;
using C = Library.Network.ClientPackets;

internal static partial class OfficialUiFullSceneTests
{
    private static void ExerciseNpcQuestServices(GameScene scene, CConnection connection, bool official,
        string evidence, string style, List<string> trace)
    {
        var window = scene.NPCQuestBox;
        var originalRewards = window.RewardGrid.Grid.ToArray();
        var originalChoices = window.ChoiceGrid.Grid.ToArray();
        var rows = window.Rows.ToArray();
        var npc = Globals.NPCInfoList.CreateNewObject(); npc.NPCName = "任务员";
        var quests = new List<QuestInfo>();
        for (int i = 0; i < 9; i++)
        {
            var quest = Globals.QuestInfoList.CreateNewObject(); quest.QuestName = "任务" + i.ToString("D2");
            quest.StartNPC = npc; quest.FinishNPC = npc;
            quest.AcceptText = "接受 [PLAYERNAME]";
            quest.ProgressText = "进行中"; quest.ArchiveText = "归档";
            quest.CompletedText = string.Join("\n", Enumerable.Range(1, 180).Select(n => "正文第" + n + "行 [PLAYERNAME]"));
            for (int j = 0; j < 8; j++)
            {
                var reward = quest.Rewards.AddNew(); reward.Quest = quest;
                reward.Item = ServiceItem(600 + i*8+j).Info; reward.Amount = j+1; reward.Choice = j >= 5;
                reward.Bound = true; reward.Duration = 60;
            }
            if (i < 3)
            {
                for (int j = 0; j < 18; j++)
                {
                    var task = quest.Tasks.AddNew(); task.Quest = quest; task.Task = QuestTaskType.KillMonster;
                    task.Amount = j+1; task.MobDescription = "进度第" + (j+1) + "项";
                }
                scene.QuestLog.Add(new ClientUserQuest { Quest=quest, QuestIndex=quest.Index, Index=quest.Index,
                    Tasks=quest.Tasks.Select(t => new ClientUserQuestTask { Task=t, TaskIndex=t.Index, Amount=t.Amount }).ToList() });
            }
            quests.Add(quest);
        }
        NpcReply(scene, connection, NpcPage("任务服务\n[离开:0]", NPCDialogType.None));
        window.NPCInfo = npc; window.BringToFront();
        NpcCheck(window.Visible && window.Rows.SequenceEqual(rows), "original six task rows retained");
        NpcCheck(window.RewardGrid.Grid.SequenceEqual(originalRewards) && window.ChoiceGrid.Grid.SequenceEqual(originalChoices), "original reward cell identities retained");
        NpcCheck(window.SelectedQuest.QuestInfo == quests[0] && window.HasChoice && window.SelectedCell == null, "completed quest selected without speculative reward");
        NpcCheck(window.CompleteButton.Visible && !window.AcceptButton.Visible, "completed quest original action visibility");
        NpcClick(scene, window.CompleteButton, DateTime.UtcNow);
        NpcCheck(connection.SendList.IsEmpty, "mandatory reward selection blocks submission");
        for (int i=0; i<5; i++)
        {
            NpcCheck(originalRewards[i].ReadOnly && originalRewards[i].Size == new Size(36,36) &&
                originalRewards[i].Item.Count == i+1, "native fixed reward amount and cell size " + i);
        }
        for (int i=0; i<3; i++)
        {
            NpcCheck(window.DisplayArea.Contains(originalChoices[i].DisplayArea), "choice cell entirely within quest frame " + i);
            NpcClick(scene, originalChoices[i], DateTime.UtcNow);
            NpcCheck(window.SelectedCell == originalChoices[i] && originalChoices[i].FixedBorder, "coordinate selection of original reward " + i);
            NpcClick(scene, window.CompleteButton, DateTime.UtcNow);
            var packet = NpcPacket<C.QuestComplete>(connection, trace);
            NpcCheck(packet.Index == quests[0].Index && packet.ChoiceIndex == quests[0].Rewards[5+i].Index, "quest/reward exact database indices " + i);
            NpcCheck(window.SelectedQuest.UserQuest != null && !window.SelectedQuest.UserQuest.Completed && window.Visible,
                "pending completion waits for server and preserves quest");
        }
        NpcClick(scene, window.Rows[1], DateTime.UtcNow);
        NpcCheck(window.SelectedQuest.QuestInfo == quests[1] && window.SelectedCell == null && originalChoices.All(c=>!c.FixedBorder), "task switch clears previous reward and border");
        NpcClick(scene, originalChoices[2], DateTime.UtcNow);
        window.UpdateQuestDisplay();
        NpcCheck(window.SelectedQuest.QuestInfo == quests[1] && window.SelectedCell == null, "refresh retains quest identity and clears stale reward");
        NpcClick(scene, originalChoices[1], DateTime.UtcNow);
        window.ScrollBar.Value = window.ScrollBar.MaxValue-window.ScrollBar.VisibleSize;
        NpcCheck(window.SelectedQuest.QuestInfo == quests[3] && window.SelectedCell == null && window.AcceptButton.Visible && !window.CompleteButton.Visible,
            "recycled list row selects visible quest and refreshes actions/rewards");
        NpcClick(scene, window.Rows.Last(), DateTime.UtcNow);
        NpcCheck(window.SelectedQuest.QuestInfo == quests.Last(), "last scrolled quest reachable");
        NpcClick(scene, window.AcceptButton, DateTime.UtcNow);
        NpcCheck(NpcPacket<C.QuestAccept>(connection, trace).Index == quests.Last().Index, "accept submits last quest identity");
        NpcCheck(!scene.QuestLog.Any(q=>q.Quest==quests.Last()), "pending accept creates no local quest");
        window.ScrollBar.Value=0; NpcClick(scene, window.Rows[0], DateTime.UtcNow);
        if (official)
        {
            foreach (var label in new[] { window.DescriptionLabel, window.TasksLabel })
            {
                var viewport = label.Parent;
                var scroll = ServiceChildren(viewport).OfType<DXVScrollBar>().Single();
                NpcCheck(viewport.Parent == window && viewport.DrawTexture && scroll.MaxValue > scroll.VisibleSize,
                    "long original quest text uses clipped scrolling viewport");
                NpcMove(new Point(viewport.DisplayArea.X+4, viewport.DisplayArea.Y+4));
                NpcCheck((DXControl.MouseControl == viewport || DXControl.MouseControl == label), "quest text wheel reaches viewport or original label");
                DXControl.MouseControl.OnMouseWheel(new MouseEventArgs(MouseButtons.None,0,0,0,-120));
                NpcCheck(scroll.Value==16, "wheel scrolls original text");
                NpcClick(scene, scroll.DownButton, DateTime.UtcNow);
                NpcCheck(scroll.Value==32, "native scroll down button works by coordinates");
                scroll.Value=scroll.MaxValue-scroll.VisibleSize;
                NpcCheck(label.Location.Y+label.Size.Height==viewport.Size.Height-1, "last content line reachable inside clipped viewport");
                NpcCheck(label.Text.Contains(label==window.DescriptionLabel ? "正文第180行" : "进度第18项"), "text end retained without truncating payload");
                if (label==window.DescriptionLabel) NpcCheck(scroll.MaxValue>2000, "long body exceeds former measurement limit");
            }
            Render(scene, Path.Combine(evidence, style+"-quest-long.png"));
            var descriptions = new[] { window.DescriptionLabel, window.TasksLabel };
            var parents = descriptions.Select(l=>l.Parent).ToArray();
            Reset(scene.Size); Render(scene, Path.Combine(evidence, style+"-quest-reset.png"));
            NpcCheck(descriptions.Select(l=>l.Parent).SequenceEqual(parents) && window.Rows.SequenceEqual(rows), "device reset retains quest controls and scroll viewports");
            NpcCheck(connection.SendList.IsEmpty, "quest reset sends no requests");
            CheckQuestBindingRestoration(scene, window.WindowSkin);
        }
        NpcClick(scene, originalChoices[0], DateTime.UtcNow);
        NpcClick(scene, scene.NPCBox.CloseButton, DateTime.UtcNow); NpcPacket<C.NPCClose>(connection, trace);
        NpcCheck(!window.Visible, "main card close hides independent quest window");
        NpcReply(scene, connection, NpcPage("再次任务\n[离开:0]", NPCDialogType.None));
        window.NPCInfo=null; window.NPCInfo=npc; window.BringToFront();
        NpcCheck(window.SelectedCell==null && window.SelectedQuest.QuestInfo==quests[0], "reopen discards old reward selection");
        NpcClick(scene, window.CompleteButton, DateTime.UtcNow);
        NpcCheck(connection.SendList.IsEmpty, "reopen again requires explicit reward selection");
        foreach (var reward in quests[0].Rewards.Where(r=>r.Choice)) reward.Class=(RequiredClass)0;
        window.UpdateQuestDisplay();
        NpcCheck(!window.HasChoice && window.SelectedCell==null && originalChoices.All(c=>c.Item==null), "class-filtered choices do not leave stale reward state");
        NpcClick(scene, window.CompleteButton, DateTime.UtcNow);
        var noChoice=NpcPacket<C.QuestComplete>(connection,trace);
        NpcCheck(noChoice.Index==quests[0].Index && noChoice.ChoiceIndex==0, "quest without applicable choice submits zero choice index");
        scene.QuestLog.First(q=>q.Quest==quests[0]).Tasks[0].Amount=0;
        window.UpdateQuestDisplay();
        window.ScrollBar.Value=window.ScrollBar.MaxValue-window.ScrollBar.VisibleSize;
        NpcClick(scene,window.Rows.Last(),DateTime.UtcNow);
        NpcCheck(window.SelectedQuest.QuestInfo==quests[0] && !window.AcceptButton.Visible && !window.CompleteButton.Visible,
            "incomplete quest keeps both accept and completion actions hidden");
        foreach (var quest in quests) { quest.StartNPC=null; quest.FinishNPC=null; }
        window.UpdateQuestDisplay();
        NpcCheck(!window.Visible && window.SelectedQuest==null && !window.HasChoice && window.SelectedCell==null,
            "empty NPC list clears selection and hides service");
        foreach (var quest in quests) { quest.StartNPC=npc; quest.FinishNPC=npc; }
        foreach (var userQuest in scene.QuestLog.Where(q=>quests.Contains(q.Quest)).ToArray()) scene.QuestLog.Remove(userQuest);
        window.NPCInfo=null;
        NpcCheck(window.SelectedQuest==null && window.SelectedCell==null && !window.HasChoice && originalRewards.All(c=>c.Item==null), "clearing NPC clears all reward/action state");
        NpcClick(scene, scene.NPCBox.CloseButton, DateTime.UtcNow); NpcPacket<C.NPCClose>(connection, trace);
    }

    private static void CheckQuestBindingRestoration(GameScene scene, GameUiWindowSkin skin)
    {
        using (var window = new NPCQuestDialog { Parent=scene, Visible=false })
        {
            var size=window.Size; var description=window.DescriptionLabel; var tasks=window.TasksLabel;
            var controls=ServiceChildren(window).ToArray(); var locations=controls.Select(c=>c.Location).ToArray();
            var type=typeof(GameUiOfficialV1WindowSet).Assembly.GetType("Client.Envir.GameUiNpcQuestBinding",true);
            var binding=(IDisposable)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[] { window },null);
            type.GetMethod("Install",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(binding,new object[] { skin });
            description.Text="保留正文"; tasks.Text="保留进度";
            bool fail=true;
            EventHandler<EventArgs> handler=(s,e)=> { if(fail) { fail=false; throw new InvalidOperationException("quest restore observer"); } };
            window.SizeChanged+=handler;
            try { binding.Dispose(); NpcCheck(false,"quest restore error must surface"); }
            catch(InvalidOperationException e) { NpcCheck(e.Message=="quest restore observer","quest restore failure surfaces"); }
            window.SizeChanged-=handler; binding.Dispose(); binding.Dispose();
            NpcCheck(window.WindowSkin==null && window.Size==size && description.Parent==window && tasks.Parent==window,
                "quest detach retry restores original shell and borrowed labels");
            NpcCheck(controls.All(c=>ServiceChildren(window).Contains(c)) && controls.Select(c=>c.Location).SequenceEqual(locations),
                "quest detach restores every original control location");
            NpcCheck(description.Size==new Size(296,80) && tasks.Size==new Size(296,80) && description.Border && tasks.Border &&
                description.Text=="保留正文" && tasks.Text=="保留进度", "quest detach preserves current text with original dimensions");
            description.Text="移除监听后仍可更新";
        }
    }
}
