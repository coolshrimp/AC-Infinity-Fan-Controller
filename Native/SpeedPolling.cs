using System;
namespace ACInfinityDesktop;
// Only fresh successful replies count toward settling; UI redraws do not.
internal sealed class SpeedPolling
{
    public int? Target { get; private set; }
    public bool Fast { get; private set; }
    public TimeSpan Interval => TimeSpan.FromSeconds(Fast ? 2 : 10);
    private int? previous;
    private DateTimeOffset lastSample, changedAt, deadline;
    private int stableSamples;
    public void Request(int target, DateTimeOffset now)
    {Target=target;Fast=true;stableSamples=0;changedAt=now;deadline=now.AddSeconds(90);}
    public void Observe(int? output, DateTimeOffset sampledAt, DateTimeOffset now, bool connected)
    {
        if(!connected){Target=null;Fast=false;previous=null;lastSample=default;stableSamples=0;return;}
        if(Fast && now>=deadline){Fast=false;Target=null;}
        if(sampledAt<=lastSample || output is null)return;
        lastSample=sampledAt;
        if(previous!=output)
        {
            if(previous.HasValue && !Fast){Fast=true;deadline=now.AddSeconds(90);}
            changedAt=now;stableSamples=1;
        }
        else stableSamples++;
        previous=output;
        bool atTarget=Target.HasValue && output==Target;
        bool settled=stableSamples>=2 && (atTarget || now-changedAt>=TimeSpan.FromSeconds(10));
        if(Fast && settled){Fast=false;Target=null;}
    }
}
