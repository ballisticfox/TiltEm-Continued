Please write the code as you were going to leave it, return after 1 year and you'd have to understand what you wrote.
It's very important that the code is clean and documented so in case someone leaves, another programmer could take and maintain it. 
Bear in mind that nobody likes to take a project where it's code looks like a dumpster.

Building needs a KSP install: copy `TiltEm.props.user.example` to `TiltEm.props.user` and
point `KSPBT_GameRoot` at it, then `dotnet build TiltEm.sln`. The build drops the mod
straight into `GameData/TiltEm`.

`Tests/` holds the suite: 250 checks over the shipped reference-frame code, plus source
pins over the rest of the mod. It runs two ways.

```
dotnet test Tests/TiltEm.Tests                   stand-in KSP types, needs no install
dotnet test Tests/TiltEm.Tests -p:KspRefs=real   the real KSP assemblies
```

The first is what CI runs, and what you will use day to day. The second is the authority:
it is what proves the stand-ins in `Tests/KspShims` have not drifted from the game, so run
it before shipping. Keep both green.

Each check is its own test, named by the defect ID it covers, so
`dotnet test --filter "DisplayName~A4"` runs just the ones for A4. The IDs are
`Docs/REFERENCE_FRAME_DEFECTS.md`.
