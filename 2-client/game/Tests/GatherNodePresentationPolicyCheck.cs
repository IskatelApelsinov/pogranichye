using System;
using Pogranichye.Client;
internal static class GatherNodePresentationPolicyCheck
{
 static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Main(){
  Require(GatherNodePresentationPolicy.IsValid("g:-2:4","herb",44.9,2),"valid node");
  Require(!GatherNodePresentationPolicy.IsValid("site:1","herb",1,1),"stable gather id");
  Require(!GatherNodePresentationPolicy.IsValid("g:1:2","herb",double.NaN,1),"finite distance");
  Require(!GatherNodePresentationPolicy.IsValid("g:1:2","herb",1,-1),"nonnegative stock");
  Require(GatherNodePresentationPolicy.CanPick(true,3,1),"server here at boundary");
  Require(!GatherNodePresentationPolicy.CanPick(false,1,1),"server here authority");
  Require(!GatherNodePresentationPolicy.CanPick(true,3.01,1),"range");
  Require(!GatherNodePresentationPolicy.CanPick(true,1,0),"depleted");
  Require(GatherNodePresentationPolicy.KindName("hive")=="дикий улей","hive label");
  Console.WriteLine("PASS: server gather-node presentation policy");
 }
}