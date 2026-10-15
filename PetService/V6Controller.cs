using PetService.Core;

namespace PetService;

internal sealed class V6Controller(Plugin plugin)
{
    private long nextUpdate;
    internal string Toast{get;private set;}="";
    internal long ToastUntil{get;private set;}
    internal void Queue(AdvancedEvent value)
    {
        if(!plugin.SharingAllowed||!plugin.Ready)return;
        value.ClientAtUtc=LocalState.Stamp(plugin.Now);
        if(plugin.Mutate(c=>{
            if(c.V6Outbox.Count>=500)throw new InvalidOperationException("Pending interaction queue is full.");
            c.V6Outbox.Add(value);
            if(c.Features.V6.Prompts.FirstOrDefault(p=>p.Id==value.PromptId) is { } prompt)V6Local.Apply(prompt,value);
        }))plugin.RequestV6Sync();
    }
    internal void Update()
    {
        if(!plugin.SharingAllowed||!plugin.Ready||Environment.TickCount64<nextUpdate)return;
        nextUpdate=Environment.TickCount64+1000;var now=plugin.Now;
        var due=plugin.Configuration.Features.V6.Routines.Any(r=>V6Local.Due(r,now).Any(d=>(r.GeneratedThroughUtc is null||string.CompareOrdinal(d,r.GeneratedThroughUtc)>0)&&!plugin.Configuration.Features.V6.Prompts.Any(p=>p.Id==V6Local.Id(r.Id+"|"+d))));
        if(due)plugin.Mutate(c=>V6Local.Refresh(c.Features.V6,now));
        foreach(var prompt in plugin.Configuration.Features.V6.Prompts.Where(p=>V6Local.Due(p,now)&&p.Policy.Delivery is "chat" or "both"&&!V6Local.Deferred(p,plugin.Observe())).ToList()) {
            if(plugin.Configuration.V6Outbox.Count>=500)break;
            var key=prompt.Id+":"+prompt.Revision;if(plugin.Configuration.V6ChatIds.Contains(key))continue;
            if(plugin.Mutate(c=>{c.V6ChatIds.Add(key);if(prompt.Policy.Delivery=="chat"&&prompt.DisplayedAtUtc is null){var e=new AdvancedEvent{PromptId=prompt.Id,Revision=prompt.Revision,Action="displayed",ClientAtUtc=LocalState.Stamp(now)};V6Local.Apply(prompt,e);c.V6Outbox.Add(e);}})) {
                MasterChatDelivery.Print(plugin.Configuration,prompt.Text);plugin.RequestV6Sync();
            }
        }
    }
    internal void NotifyChanges()
    {
        if(!plugin.SharingAllowed)return;
        var state=plugin.Configuration.Features.V6;
        var entries=state.Points.Entries.Where(e=>!plugin.Configuration.PointNoticeIds.Contains(e.Id)&&e.Source!="legacy").ToList();
        var requests=state.Redemptions.Where(r=>r.State is "approved" or "declined"&&!plugin.Configuration.RedemptionNoticeIds.Contains(r.Id)).ToList();
        if(entries.Count==0&&requests.Count==0)return;
        if(!plugin.Mutate(c=>{foreach(var e in state.Points.Entries)c.PointNoticeIds.Add(e.Id);foreach(var r in requests)c.RedemptionNoticeIds.Add(r.Id);}))return;
        var messages=new List<string>();
        if(entries.Count<=3)foreach(var entry in entries.OrderBy(e=>e.AtUtc,StringComparer.Ordinal))messages.Add((entry.Delta>=0?"+":"")+entry.Delta+" "+(entry.Kind=="reward"?"Reward":"Punishment")+" Points"+(entry.Note.Length>0?" — "+entry.Note:"")+". Balance: "+entry.Balance);
        else messages.Add(entries.Count+" recent point changes. Reward Points: "+state.Points.Reward+" · Punishment Points: "+state.Points.Punishment);
        foreach(var request in requests)messages.Add("Reward request “"+request.Name+"” "+request.State+(request.Note.Length>0?" — "+request.Note:""));
        foreach(var message in messages)if(plugin.Configuration.PointChatNotifications)MasterChatDelivery.Print(plugin.Configuration,message);
        if(plugin.Configuration.PointToastNotifications){Toast=string.Join("\n",messages);ToastUntil=Environment.TickCount64+10000;}
    }
    internal DynamicSettings EffectiveSpeech()
    {
        var saved=plugin.Configuration.Dynamic;var gag=plugin.Configuration.Features.V6.Gag;
        if(!gag.Configured)return saved;
        return new(){GarbleEnabled=gag.Enabled&&(gag.ExpiresAtUtc is null||LocalState.Parse(gag.ExpiresAtUtc)>plugin.Now),GarbleStrength=gag.Strength,GarbleStyle=gag.Style,CustomSyllables=gag.Syllables,ExcludedChannels=gag.ExcludedChannels};
    }
}
