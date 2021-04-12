# README.md

## What is NeverAway?

Neveraway is a small utility that will prevent your computer from showing as away in Teams or other chat tools.

## How does it work?

As of v2.0, it executes the KeyUp event on F24 key every 10 seconds.

Very old (pre-codeplex) versions used a testing automation tool to simulate key presses. Release versions v1.0 and v1.1 used [InputSimulator](https://www.nuget.org/packages/InputSimulator/) and would simulate an entire keypress rather than just KeyUp. A full KeyPress doesn't appear to be needed though, so while revisiong v2.0, I switched over to simply making user32.dll calls directly.

I wrote a small wrapper class called [KeyboardWrapper](https://github.com/royashbrook/KeyboardWrapper) but this project just uses the user32.dll call directly since I only need that one call. I did publish a nuget package for [KeyboardWrapper](https://github.com/royashbrook/KeyboardWrapper) in case anyone finds it useful. =)

## Why does this exist?

From the codeplex archive:

>This is a simple program that sits in your toolbar and will press a key every so often. It was purpose built to ensure I never showed up as 'away' in MOC. =)

I wrote that in January of 2013 when I published v1.0, but there was a little more history.

I believe I wrote the first iteration of this as a script to run in [LINQPad](https://www.linqpad.net/) around 2010/2011

I had transitioned into a management role and spent the vast majority of my time on the phone or in meetings. I was having difficulty figuring out how to quanitify how much work I was doing, or at least figuring how much of my time was productive. Previously, it was a bit easier since it was very task based. My measures as a manager were based on my teams accomplishments and product deliveries. I liked that, but was looking for a way to quanitify for myself my productive time.

To that end, I started using a tool called [RescueTime](https://www.rescuetime.com/). I liked it, but I found that a lot of times on the phone, my system would show as 'idle' so I started looking for a way to keep my computer from going into screen saver mode. I think at the time I used [Selenium](https://www.selenium.dev/) to do something with a vbs script or a little C# program.

Over time, I noticed a bigger problem if I didn't run my script. I could not control my away status without some tool like this. And frequently people from my team would want to reach out to me, but I would show as 'Away' because I had been on the phone for more than 10 minutes or someone was in my office for awhile and we were drawing on a whiteboard or something similar. This was in the Microsoft Office Communicator days and I was not able to control my away time as it was set by policy. So this eventually supplanted my original issue with the productivity tracker and became the bigger one to solve.

So I looked for a way to automatically send keys in and found things like SendKeys and other items. The testing tool I had used originally was commercial so that was not something I wanted to use. And things like SendKeys and some methods for programatically preventing the system from sleeping did not stop you from showing as away.

After some time testing, I seemed to discover that I needed to simulate a key press and I used [InputSimulator](https://www.nuget.org/packages/InputSimulator/) for this. I didn't really try too hard at that time to find another way, I was just testing different things and finally found this one that worked and then over time understood that it was the actual simulated key presses that seemed to work, not just sending keys to the screen or active window like sendkeys or some other input simulator. It was the keypress event that seemed to do the trick.

### Note

There is a great product out there called [Caffeine](https://www.zhornsoftware.co.uk/caffeine/) for preventing your computer from going to sleep. I found it not long after I started looking to make something, but as it was primarily focused on preventing the computer from sleeping, and as it wasn't mine, I didn't use it. Initially I was thinking I could just get a little script running that would work. And I later did, but I decided to just make an app at that time instead.

When I decided to refresh this program, I noticed that the InputSimulator library was giving me warnings on compile as it was not meant to run on dotnet5. While looking for details on how to simulate the key presses myself, I came across a number of articles about going away issues on Teams and many references to Caffeine. So I figured I would mention it here since it's a great product even though they aren't related. I never used Caffeine, but the fact that it's still going strong after all of these years and has tons of updates I think deserves a reference.

Caffeine uses F15 instead of F24. I picked F24 just because I figured no one would ever have that on their keyboard. I can't say if it's better, but it's always worked for me. Caffeine also has some different methods for preventing sleep like setting STES and allowing the screensaver which will allow your computer to simply not go to sleep.

You can checkout the Caffeine site for more details.

While NeverAway will prevent your computer from going to sleep, the real purpose is to make sure you don't show as Away. I also didn't care about the system going to sleep if you locked your screen or something, I just didn't want to show Away when I was sitting in front of my computer.