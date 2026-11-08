# Ishikawa

## Simple

**Input:**
```
ishikawa-beta
    Blurry Photo
    Process
        Out of focus
        Shutter speed too slow
    User
        Shaky hands
    Equipment
        Dirty lens
    Environment
        Too dark
```
**Rendered by Naiad:**

<p align="center">
  <img src="../Tests/Ishikawa/IshikawaTests.Simple.verified.png" />
</p>


[Open in Mermaid Live](https://mermaid.live/edit#base64:eyJjb2RlIjoiaXNoaWthd2EtYmV0YVxuICAgIEJsdXJyeSBQaG90b1xuICAgIFByb2Nlc3NcbiAgICAgICAgT3V0IG9mIGZvY3VzXG4gICAgICAgIFNodXR0ZXIgc3BlZWQgdG9vIHNsb3dcbiAgICBVc2VyXG4gICAgICAgIFNoYWt5IGhhbmRzXG4gICAgRXF1aXBtZW50XG4gICAgICAgIERpcnR5IGxlbnNcbiAgICBFbnZpcm9ubWVudFxuICAgICAgICBUb28gZGFyayIsIm1lcm1haWQiOnsidGhlbWUiOiJkZWZhdWx0In19)

## Nested

**Input:**
```
ishikawa-beta
    Blurry Photo
    Process
        Out of focus
        Shutter speed too slow
        Protective film not removed
        Beautification filter applied
    User
        Shaky hands
    Equipment
        LENS
            Inappropriate lens
            Damaged lens
            Dirty lens
        SENSOR
            Damaged sensor
            Dirty sensor
    Environment
        Subject moved too quickly
        Too dark
```
**Rendered by Naiad:**

<p align="center">
  <img src="../Tests/Ishikawa/IshikawaTests.Nested.verified.png" />
</p>


[Open in Mermaid Live](https://mermaid.live/edit#base64:eyJjb2RlIjoiaXNoaWthd2EtYmV0YVxuICAgIEJsdXJyeSBQaG90b1xuICAgIFByb2Nlc3NcbiAgICAgICAgT3V0IG9mIGZvY3VzXG4gICAgICAgIFNodXR0ZXIgc3BlZWQgdG9vIHNsb3dcbiAgICAgICAgUHJvdGVjdGl2ZSBmaWxtIG5vdCByZW1vdmVkXG4gICAgICAgIEJlYXV0aWZpY2F0aW9uIGZpbHRlciBhcHBsaWVkXG4gICAgVXNlclxuICAgICAgICBTaGFreSBoYW5kc1xuICAgIEVxdWlwbWVudFxuICAgICAgICBMRU5TXG4gICAgICAgICAgICBJbmFwcHJvcHJpYXRlIGxlbnNcbiAgICAgICAgICAgIERhbWFnZWQgbGVuc1xuICAgICAgICAgICAgRGlydHkgbGVuc1xuICAgICAgICBTRU5TT1JcbiAgICAgICAgICAgIERhbWFnZWQgc2Vuc29yXG4gICAgICAgICAgICBEaXJ0eSBzZW5zb3JcbiAgICBFbnZpcm9ubWVudFxuICAgICAgICBTdWJqZWN0IG1vdmVkIHRvbyBxdWlja2x5XG4gICAgICAgIFRvbyBkYXJrIiwibWVybWFpZCI6eyJ0aGVtZSI6ImRlZmF1bHQifX0=)

## DeepHierarchy

**Input:**
```
ishikawa
    Late delivery
    Logistics
        Carrier
            Truck breakdown
                No maintenance schedule
                    Budget cuts
        Routing
    Warehouse
        Picking errors
```
**Rendered by Naiad:**

<p align="center">
  <img src="../Tests/Ishikawa/IshikawaTests.DeepHierarchy.verified.png" />
</p>


[Open in Mermaid Live](https://mermaid.live/edit#base64:eyJjb2RlIjoiaXNoaWthd2FcbiAgICBMYXRlIGRlbGl2ZXJ5XG4gICAgTG9naXN0aWNzXG4gICAgICAgIENhcnJpZXJcbiAgICAgICAgICAgIFRydWNrIGJyZWFrZG93blxuICAgICAgICAgICAgICAgIE5vIG1haW50ZW5hbmNlIHNjaGVkdWxlXG4gICAgICAgICAgICAgICAgICAgIEJ1ZGdldCBjdXRzXG4gICAgICAgIFJvdXRpbmdcbiAgICBXYXJlaG91c2VcbiAgICAgICAgUGlja2luZyBlcnJvcnMiLCJtZXJtYWlkIjp7InRoZW1lIjoiZGVmYXVsdCJ9fQ==)

## CausesWithoutSubCauses

**Input:**
```
ishikawa-beta
    Server outage
    Power
    Network
    Hardware
```
**Rendered by Naiad:**

<p align="center">
  <img src="../Tests/Ishikawa/IshikawaTests.CausesWithoutSubCauses.verified.png" />
</p>


[Open in Mermaid Live](https://mermaid.live/edit#base64:eyJjb2RlIjoiaXNoaWthd2EtYmV0YVxuICAgIFNlcnZlciBvdXRhZ2VcbiAgICBQb3dlclxuICAgIE5ldHdvcmtcbiAgICBIYXJkd2FyZSIsIm1lcm1haWQiOnsidGhlbWUiOiJkZWZhdWx0In19)

## EffectOnly

**Input:**
```
ishikawa-beta
    A rather long problem statement that has to wrap
```
**Rendered by Naiad:**

<p align="center">
  <img src="../Tests/Ishikawa/IshikawaTests.EffectOnly.verified.png" />
</p>


[Open in Mermaid Live](https://mermaid.live/edit#base64:eyJjb2RlIjoiaXNoaWthd2EtYmV0YVxuICAgIEEgcmF0aGVyIGxvbmcgcHJvYmxlbSBzdGF0ZW1lbnQgdGhhdCBoYXMgdG8gd3JhcCIsIm1lcm1haWQiOnsidGhlbWUiOiJkZWZhdWx0In19)

## CommentsAndBlankLines

**Input:**
```
%% leading comment
ishikawa-beta
    Problem

    %% a comment between causes
    Cause A
        Sub-cause A1
    Cause B<br>on two lines
```
**Rendered by Naiad:**

<p align="center">
  <img src="../Tests/Ishikawa/IshikawaTests.CommentsAndBlankLines.verified.png" />
</p>


[Open in Mermaid Live](https://mermaid.live/edit#base64:eyJjb2RlIjoiJSUgbGVhZGluZyBjb21tZW50XG5pc2hpa2F3YS1iZXRhXG4gICAgUHJvYmxlbVxuXG4gICAgJSUgYSBjb21tZW50IGJldHdlZW4gY2F1c2VzXG4gICAgQ2F1c2UgQVxuICAgICAgICBTdWItY2F1c2UgQTFcbiAgICBDYXVzZSBCXHUwMDNDYnJcdTAwM0VvbiB0d28gbGluZXMiLCJtZXJtYWlkIjp7InRoZW1lIjoiZGVmYXVsdCJ9fQ==)

