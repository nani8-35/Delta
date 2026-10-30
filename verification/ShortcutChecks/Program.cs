using System;
using Delta.Native;
int checks=0;
void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
foreach(var shortcut in ShortcutCatalog.All){
 var keys=new ShortcutKeyState();int calls=0;
 bool Dispatch(uint key,uint modifiers){var action=ShortcutCatalog.ActionFor(key,modifiers);if(action==null)return false;Check(action==shortcut.Action,shortcut.Display+" mapping");calls++;return true;}
 Check(keys.KeyDown(shortcut.Key,shortcut.Modifiers,Dispatch),shortcut.Display+" consumed");
 Check(keys.KeyDown(shortcut.Key,shortcut.Modifiers,Dispatch),"repeat consumed");
 Check(keys.KeyDown(shortcut.Key,0,Dispatch),"repeat after modifier release consumed");
 Check(calls==1,"one dispatch per press");
 Check(keys.KeyUp(shortcut.Key),"release consumed");
 Check(!keys.KeyUp(shortcut.Key),"unmatched release passed");
 Check(keys.KeyDown(shortcut.Key,shortcut.Modifiers,Dispatch),"second press consumed");
 Check(calls==2,"second press dispatched");
 Check(keys.KeyUp(shortcut.Key),"second release consumed");
}
var state=new ShortcutKeyState();
bool Known(uint key,uint mods)=>ShortcutCatalog.ActionFor(key,mods)!=null;
Check(!state.KeyDown(0x41,2,Known),"Ctrl+A remains available");
Check(!state.KeyUp(0x41),"Ctrl+A release available");
Check(!state.KeyDown(0x54,10,Known),"extra Windows modifier is not Ctrl+T");
Check(!state.KeyUp(0x54),"unrecognized key release available");
Check(!state.KeyDown(0x42,0,Known),"ordinary B passes");
Check(!state.KeyDown(0x42,1,Known),"held B cannot acquire shortcut on repeat");
Check(!state.KeyUp(0x42),"ordinary B release passes");
Console.WriteLine($"Passed {checks} shortcut routing and key lifecycle checks across {ShortcutCatalog.All.Count} catalog entries.");

// Modifier buffering regression checks, including both modifier-release orders.
foreach(var shortcut in ShortcutCatalog.All){
 foreach(bool releaseModifiersFirst in new[]{false,true}){
  var buffered=new BufferedShortcutState();int dispatched=0;
  var replayed=new System.Collections.Generic.List<ShortcutKeyEvent>();
  bool Dispatch(uint key,uint mods){var action=ShortcutCatalog.ActionFor(key,mods);if(action==null)return false;Check(action==shortcut.Action,"buffered action mapping");dispatched++;return true;}
  void Replay(System.Collections.Generic.IReadOnlyList<ShortcutKeyEvent> events)=>replayed.AddRange(events);
  var modifierKeys=new System.Collections.Generic.List<uint>();
  if((shortcut.Modifiers&1)!=0)modifierKeys.Add(0xA4);
  if((shortcut.Modifiers&2)!=0)modifierKeys.Add(0xA2);
  if((shortcut.Modifiers&4)!=0)modifierKeys.Add(0xA0);
  foreach(uint key in modifierKeys)Check(buffered.Handle(new(key,0,false,false),Dispatch,Replay),"initial modifier buffered");
  Check(replayed.Count==0,"no initial modifier leaked");
  Check(buffered.Handle(new(shortcut.Key,0,false,false),Dispatch,Replay),"trigger consumed");
  Check(buffered.Handle(new(shortcut.Key,0,false,false),Dispatch,Replay),"repeat consumed");
  Check(dispatched==1,"one buffered dispatch");
  if(releaseModifiersFirst)foreach(uint key in modifierKeys)Check(buffered.Handle(new(key,0,false,true),Dispatch,Replay),"early modifier up consumed");
  Check(buffered.Handle(new(shortcut.Key,0,false,true),Dispatch,Replay),"trigger up consumed");
  if(!releaseModifiersFirst)foreach(uint key in modifierKeys)Check(buffered.Handle(new(key,0,false,true),Dispatch,Replay),"modifier up consumed");
  Check(replayed.Count==0,"complete Delta chord generated no replay");
 }
}
void TestPassthrough(uint modifier,uint key){
 var buffered=new BufferedShortcutState();var output=new System.Collections.Generic.List<ShortcutKeyEvent>();
 void Replay(System.Collections.Generic.IReadOnlyList<ShortcutKeyEvent> events)=>output.AddRange(events);
 Check(buffered.Handle(new(modifier,0,false,false),Known,Replay),"ordinary modifier buffered");
 Check(buffered.Handle(new(key,0,false,false),Known,Replay),"ordinary chord replayed");
 Check(output.Count==2&&output[0].Key==modifier&&output[1].Key==key&&!output[0].Up&&!output[1].Up,"modifier replay precedes trigger");
 Check(!buffered.Handle(new(key,0,false,true),Known,Replay),"ordinary trigger up passed");
 Check(buffered.Handle(new(modifier,0,false,true),Known,Replay),"forwarded modifier up replayed");
 Check(output.Count==3&&output[2].Up&&output[2].Key==modifier,"forwarded modifier released");
}
TestPassthrough(0xA4,0x09); // Alt+Tab
TestPassthrough(0xA2,0x43); // Ctrl+C
TestPassthrough(0xA0,0x41); // Shift+A
TestPassthrough(0x5B,0x45); // Win+E
{
 var buffered=new BufferedShortcutState();var output=new System.Collections.Generic.List<ShortcutKeyEvent>();
 void Replay(System.Collections.Generic.IReadOnlyList<ShortcutKeyEvent> events)=>output.AddRange(events);
 Check(buffered.Handle(new(0xA4,0,false,false),Known,Replay),"standalone Alt buffered");
 Check(buffered.Handle(new(0xA4,0,false,true),Known,Replay),"standalone Alt replayed");
 Check(output.Count==2&&!output[0].Up&&output[1].Up,"standalone Alt tap preserved");
}
{
 var buffered=new BufferedShortcutState();var output=new System.Collections.Generic.List<ShortcutKeyEvent>();
 void Replay(System.Collections.Generic.IReadOnlyList<ShortcutKeyEvent> events)=>output.AddRange(events);
 buffered.ObserveInjectedModifier(new(0xA2,0,false,false));
 buffered.Handle(new(0xA5,0,true,false),Known,Replay);
 Check(buffered.Handle(new(0x42,0,false,false),Known,Replay),"AltGr+B replayed rather than shortcut");
 Check(output.Count==2&&output[0].Key==0xA5,"AltGr retains forwarded Ctrl");
 buffered.ObserveInjectedModifier(new(0xA2,0,false,true));
 buffered.Handle(new(0xA5,0,true,true),Known,Replay);
 Check(buffered.ReleaseForwarded().Count==0,"AltGr modifier cleanup");
}
{
 var buffered=new BufferedShortcutState();var output=new System.Collections.Generic.List<ShortcutKeyEvent>();
 void Replay(System.Collections.Generic.IReadOnlyList<ShortcutKeyEvent> events)=>output.AddRange(events);
 buffered.Handle(new(0xA2,0,false,false),Known,Replay);
 Check(buffered.ReleaseForwarded().Count==0,"unused suppressed modifier never released to background");
 buffered.SeedForwardedModifier(0xA2);
 var release=buffered.ReleaseForwarded();
 Check(release.Count==1&&release[0].Up,"shutdown releases a forwarded modifier");
}
Console.WriteLine($"Passed {checks} total checks including modifier buffering, unrelated shortcuts, AltGr, and shutdown cleanup.");
