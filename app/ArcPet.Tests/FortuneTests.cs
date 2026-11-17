using System;
using System.Collections.Generic;
using ArcPet.Animation;
using ArcPet.Behavior;

internal static class FortuneTests
{
    internal static void Run(string assets, Action<bool,string> check)
    {
        var six = new DateTime(2026, 11, 15, 6, 0, 0, DateTimeKind.Local);
        var before = six.AddMilliseconds(-1);
        check(DailyFortune.DayFor(before) == six.Date.AddDays(-1) && DailyFortune.DayFor(six) == six.Date,
            "fortune date rolls over precisely at 06:00 local time");
        check(DailyFortune.ResultFor(six) == DailyFortune.ResultFor(six.AddDays(1).AddMilliseconds(-1)),
            "fortune stays stable through midnight until next 06:00");
        var results = new HashSet<string>();
        for (int i = 0; i < 90; i++) results.Add(DailyFortune.ResultFor(six.AddDays(i)));
        check(results.SetEquals(new[] { "大吉", "吉", "中吉", "小吉", "末吉", "凶", "大凶" }),
            "the daily draw supports all seven grades");

        var clips = new JsonAnimationCatalog(assets).LoadAll();
        var pet = new PetBehaviorController(clips, new RandomSource());
        pet.Start(0);
        check(pet.TryDrawFortune(0, before) && pet.CurrentClip.DurationMilliseconds == 2 * clips["fortune_shake"].DurationMilliseconds,
            "right-click draw requests two complete shake loops");
        string result = pet.FortuneResult;
        check(!pet.TryDrawFortune(1, six), "a second click cannot replace an active draw");
        long shakeEnd = pet.CurrentClip.DurationMilliseconds;
        pet.PrimaryDown(100);
        pet.Tick(shakeEnd - 1);
        check(pet.Phase == "fortune_shake", "other gestures do not cut the shaking phase");
        pet.Tick(shakeEnd);
        check(pet.Phase == "fortune_draw", "lifting the stick follows shaking");
        long drawEnd = shakeEnd + pet.CurrentClip.DurationMilliseconds;
        pet.Tick(drawEnd);
        check(pet.Phase == "fortune_reveal" && pet.FortuneResult == result,
            "result becomes ready for the bubble after lifting and opening eyes");
        check(pet.CurrentClip.Frames[0].AssetPath ==
            clips["fortune_draw"].Frames[clips["fortune_draw"].Frames.Count - 1].AssetPath,
            "reveal uses the last open-eye pose");
        long done = drawEnd + pet.CurrentClip.DurationMilliseconds;
        pet.Tick(done);
        check(pet.Phase == "idle" && pet.FortuneResult == null && pet.CanDrawFortune,
            "result clears and draw is available again after the hold");
        check(pet.TryDrawFortune(done, before) && pet.FortuneResult == result,
            "replaying before the cutoff gives the same result");

        var restarted = new PetBehaviorController(clips, new RandomSource());
        restarted.Start(0);
        check(restarted.TryDrawFortune(0, before) && restarted.FortuneResult == result,
            "restart does not change the result for that local draw date");
    }
}
