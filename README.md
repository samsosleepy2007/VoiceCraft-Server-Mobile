# VoiceCraft Server Mobile

VoiceCraft Server Mobile คือการนำ **VoiceCraft v1.7.1** มาปรับให้สามารถทำงานเป็น VoiceCraft Server บน Android ได้ พร้อมระบบเชื่อมต่อ Minecraft Bedrock ผ่าน **Endstone**, ระบบ **Render Relay** สำหรับ control plane, ระบบ **Primary/Backup Relay failover**, และระบบ **Account / Free Account** สำหรับแอปมือถือ

README นี้เน้นอธิบายว่าแต่ละระบบทำงานอย่างไรและข้อมูลไหลผ่านส่วนต่าง ๆ แบบไหน เพื่อให้สามารถเข้าใจทั้งภาพรวมและรายละเอียดเชิงเทคนิคของโปรเจกต์ได้ในหน้าเดียว

## เวอร์ชันปัจจุบัน

| Component | Version |
|---|---|
| Android app | `1.7.1-android-phase2-ui4.5.1-account-v2-guest` |
| Android version code | `12` |
| Endstone plugin | `0.2.6` |
| Render Relay | `0.2.1` |
| Bridge protocol | `1` |
| VoiceCraft upstream | `v1.7.1` |
| Upstream pinned commit | `85aaccccbb58adb23e8c87144e8b1c24bf4b2011` |

> VoiceCraft audio/wire protocol ของต้นฉบับไม่ได้ถูกเปลี่ยน การส่งเสียงยังเป็น VoiceCraft/LiteNetLib UDP โดยตรงไปยัง Android ส่วน Render Relay ใช้สำหรับข้อมูลควบคุม สถานะผู้เล่น และ binding เท่านั้น

---

# ภาพรวมการทำงาน

ระบบแบ่งออกเป็น 5 ส่วนหลัก

```text
┌───────────────────────────────┐
│ Minecraft Bedrock / MCSV      │
│                               │
│ Endstone VoiceCraft 0.2.6     │
│ - อ่านตำแหน่งผู้เล่น           │
│ - /vc                         │
│ - Bind / Unbind               │
└───────────────┬───────────────┘
                │ WSS control plane
                ▼
┌───────────────────────────────┐
│ Render Relay 0.2.1            │
│                               │
│ Primary Relay                 │
│ Backup Relay #1               │
│ Backup Relay #2 ...           │
│                               │
│ - แยก room ด้วย server_id      │
│ - cache player state          │
│ - forward control messages    │
└───────────────┬───────────────┘
                │ WSS control plane
                ▼
┌───────────────────────────────┐
│ VoiceCraft Server Mobile      │
│ Android                       │
│                               │
│ Account Gate                  │
│ Foreground Service            │
│ Endstone Bridge Controller    │
│ VoiceCraft v1.7.1 Runtime     │
└───────────────┬───────────────┘
                │ VoiceCraft UDP
                ▼
┌───────────────────────────────┐
│ VoiceCraft Client             │
│                               │
│ - microphone                  │
│ - Opus audio                  │
│ - positioning = Server        │
└───────────────────────────────┘
```

สิ่งสำคัญคือ **เส้นทางเสียง** และ **เส้นทางข้อมูล Minecraft** แยกออกจากกัน

```text
เสียงจริง
VoiceCraft Client ───── UDP ─────> Android VoiceCraft Runtime

ข้อมูล Minecraft / Bind / Relay state
Minecraft + Endstone ── WSS ──> Render Relay ── WSS ──> Android
```

ดังนั้น Render ไม่ได้ทำหน้าที่เป็น audio relay ในเวอร์ชันนี้

---

# 1. ระบบ Account และ Free Account

ก่อนเข้าหน้าหลัก แอปจะเปิด `AccountGateActivity` เป็น launcher หลัก

ผู้ใช้มี 2 รูปแบบ

```text
Registered VoiceCraft Account
หรือ
Free Account แบบ local บนอุปกรณ์
```

## Registered Account

Registered Account ใช้สำหรับ session ของบัญชี VoiceCraft ที่มีอยู่แล้ว

ลำดับโดยย่อ

```text
เปิดแอป
  │
  ▼
Account Gate
  │
  ▼
Login ด้วย username/password
  │
  ▼
Account API ตรวจสอบข้อมูล
  │
  ▼
บันทึก registered session แบบเข้ารหัสในเครื่อง
  │
  ▼
เข้า VoiceCraft Server Mobile
```

แอปมือถือไม่ได้เปิด public registration ภายในตัวแอป และไม่ได้แสดงระบบจัดการสิทธิ์ภายในหน้า Login

## Free Account / Guest

Free Account ถูกออกแบบให้เป็นบัญชี local ที่ไม่จำเป็นต้องสร้าง account row ในฐานข้อมูลบัญชีหลัก

เมื่อสร้าง Free Account ครั้งแรก แอปจะสร้าง Guest ID เช่น

```text
GUEST-xxxxxxxx...
```

ข้อมูล local ถูกเก็บด้วยหลักการดังนี้

```text
Guest profile/session
        │
        ▼
JSON
        │
        ▼
AES-GCM encryption
        │
        ▼
Key จาก Android Keystore
        │
        ▼
NoBackupFilesDir
```

ผลลัพธ์คือ

- ปิดและเปิดแอปใหม่ Guest ID เดิมยังอยู่
- อัปเดต APK Guest ID เดิมยังอยู่
- Android cloud backup ไม่ควรย้าย Guest profile ไปเครื่องใหม่
- Clear App Data จะลบ Guest ID
- ถอนการติดตั้งแล้วลงใหม่จะได้ Guest ID ใหม่

ระบบ Guest backend ใช้ session แบบ stateless และแยกออกจากตารางบัญชีหลัก เพื่อไม่ให้ Free Account กลายเป็น account row ปกติ

---

# 2. Android VoiceCraft Server Runtime

ส่วนสำคัญที่สุดของโปรเจกต์คือการทำให้ VoiceCraft Server ของต้นฉบับสามารถทำงานบน Android แบบ headless ได้

VoiceCraft ต้นฉบับถูก pin ไว้เป็น submodule ที่ commit

```text
85aaccccbb58adb23e8c87144e8b1c24bf4b2011
```

ระหว่าง build ระบบ CI จะ apply Android compatibility patches ก่อนสร้าง APK

ตัวอย่างสิ่งที่ patch เพิ่มหรือเปลี่ยน

- เปิด VoiceCraft Server ในโหมด headless
- ปิด dependency ของ console UI ที่ไม่เหมาะกับ Android
- ปรับ McHttp สำหรับ Android ให้ใช้ TCP listener ที่ทำงานได้บน platform นี้
- เชื่อม Runtime Dispatcher เข้ากับ Android service
- เพิ่ม Endstone bridge controller
- เพิ่ม Account / Guest UI และ storage

## Foreground Service

เมื่อผู้ใช้กดเริ่ม Server แอปจะเปิด

```text
VoiceCraftServerService
```

เป็น Android Foreground Service

service นี้ทำหน้าที่

- รัน VoiceCraft server ต่อเนื่องเบื้องหลัง
- แสดง notification สถานะ server
- รักษา process ให้เหมาะกับงาน server ระยะยาว
- ใช้ Partial Wake Lock เพื่อช่วยไม่ให้ CPU หลับระหว่าง server ทำงาน
- เปิด VoiceCraft runtime
- เปิด Endstone Bridge Controller
- อัปเดตจำนวน client / relay state บน notification

ก่อน start จริง ระบบจะตรวจ

```text
Voice Port
Server Key
Primary Relay URL
Server ID
Bridge Secret
```

ถ้า Primary Relay configuration ไม่สมบูรณ์ server จะไม่เริ่ม และจะแสดงสาเหตุให้แก้ก่อน

---

# 3. VoiceCraft Runtime และเส้นทางเสียง

เมื่อ service พร้อม จะเรียก VoiceCraft v1.7.1 runtime โดยตรง

ค่าหลักปัจจุบัน

```text
Transport mode : McHttp / HTTP compatibility
Transport host : 0.0.0.0
Voice UDP port : ค่าเดียวกับ Voice Port
Default port   : 9050
```

Android จะเปิดทั้ง VoiceCraft UDP listener และ McHttp compatibility listener ตาม configuration ของ runtime

## เส้นทางเสียง

เสียงไม่ได้วิ่งผ่าน Minecraft, Endstone หรือ Render

```text
Microphone
   │
   ▼
VoiceCraft Client
   │
   │ VoiceCraft / LiteNetLib UDP
   ▼
VoiceCraft Server บน Android
   │
   ▼
Voice routing / positioning / effect logic
   │
   ▼
VoiceCraft Client อื่นที่ควรได้ยิน
```

VoiceCraft Runtime เป็นผู้ตัดสิน routing ของเสียงโดยใช้ entity state ที่ Android ได้รับจาก Minecraft bridge

ดังนั้น Endstone มีหน้าที่ส่ง **สถานะของโลก Minecraft** เข้ามา แต่ไม่ได้ส่ง audio frame

---

# 4. VoiceCraft Entity และ Server Positioning

เมื่อ VoiceCraft Client เชื่อมต่อ Android และใช้

```text
Positioning Type = Server
```

Android Bridge Controller จะตรวจพบ `VoiceCraftNetworkEntity` ใหม่

ถ้า entity ยังไม่ bind ระบบจะ

1. สร้าง Binding Key แบบสุ่ม 5 ตัว
2. ผูก Binding Key กับ VoiceCraft entity ชั่วคราว
3. เปลี่ยน description ของ client ให้แสดง Binding Key
4. รอ Minecraft player นำ key นั้นไป bind ผ่าน `/vc`

ตัวอย่างแนวคิด

```text
Voice Client connects
      │
      ▼
VoiceCraft entity #37
      │
      ▼
Binding Key = aB31x
      │
      ▼
"Welcome! Your binding key is aB31x"
```

หลัง bind สำเร็จ entity เดิมจะถูกเชื่อมกับ Minecraft player จริง

จากนั้นตำแหน่งของ VoiceCraft entity จะไม่ใช้ตำแหน่งจาก client เอง แต่จะตาม state ที่ Endstone ส่งมา

```text
Minecraft Player
X Y Z
Pitch / Yaw
Dimension
Name
      │
      ▼
Endstone
      │
      ▼
Render Relay
      │
      ▼
Android Bridge Controller
      │
      ▼
VoiceCraft Entity
```

ค่า Minecraft rotation ถูก map เป็น VoiceCraft rotation และ dimension ถูก map เป็น `WorldId`

นี่คือหัวใจที่ทำให้ proximity voice รู้ว่าใครอยู่ใกล้ใครในโลก Minecraft

---

# 5. Endstone Plugin

`VoiceCraft.Endstone` ทำงานอยู่ฝั่ง Minecraft Bedrock server

เวอร์ชันปัจจุบัน

```text
Endstone plugin 0.2.6
Validated with Endstone 0.11.10
```

หน้าที่หลักคือ

- ตรวจผู้เล่นเข้า/ออก
- อ่านตำแหน่งและ rotation
- ส่ง player state
- เปิด UI `/vc`
- ส่ง bind/unbind request
- รับผล bind/unbind จาก Android
- ติดตาม Relay / Android peer state
- ส่ง snapshot ใหม่เมื่อ Android ขอ
- ทำ Auto Rebind เมื่อ VoiceCraft Client หลุดแบบไม่ตั้งใจ

## Player State

Endstone ส่งข้อมูลในรูปแบบแนวคิด

```text
player_state
├─ xuid / uuid
├─ player name
├─ dimension
├─ x / y / z
├─ pitch
└─ yaw
```

ระบบไม่จำเป็นต้องส่ง state ทุกอย่างแบบไร้เงื่อนไข เพราะมีค่า threshold สำหรับ position/rotation เพื่อลด traffic ที่ไม่จำเป็น

ค่าตั้งต้นตัวอย่าง

```text
interval_ticks = 2
position_epsilon = 0.05
rotation_epsilon = 1.0
heartbeat_seconds = 30
```

---

# 6. `/vc` และระบบ Binding

คำสั่งหลักที่ผู้เล่นใช้คือ

```text
/vc
```

เมนูจะเปลี่ยนตามสถานะ

```text
ยังไม่ Bind
   └─ Bind Microphone

กำลัง Bind
   └─ Cancel Pending Bind

Bind แล้ว
   └─ Disconnect / Unbind Microphone
```

## Initial Bind

ลำดับเต็ม

```text
1. VoiceCraft Client ต่อเข้า Android
2. Android สร้าง VoiceCraft entity
3. Android สร้าง Binding Key
4. Client แสดง Binding Key
5. ผู้เล่นเปิด /vc ใน Minecraft
6. ผู้เล่นกรอก Binding Key
7. Endstone สร้าง bind request + requestId
8. Render ส่ง request ไป Android
9. Android ตรวจว่า key ยังมีอยู่และยังไม่ถูกใช้
10. Android ตรวจว่า entity ยังมีชีวิตและใช้ Server positioning
11. Android map Minecraft player -> VoiceCraft entity
12. Android apply position/rotation/dimension ล่าสุด
13. Android ส่ง bind_result กลับ
14. Endstone เปลี่ยนสถานะผู้เล่นเป็น Bound
```

Binding Key เป็น one-use key เมื่อ bind สำเร็จแล้ว key จะถูกนำออกจากรายการ waiting keys

---

# 7. Manual Unbind ที่ตัด Voice Client จริง

ระบบ Disconnect / Unbind ไม่ใช่แค่ลบสถานะใน Minecraft

มันสั่ง disconnect VoiceCraft NetPeer จริง

```text
Player กด Disconnect
        │
        ▼
Endstone
สร้าง unbind(requestId, entityId)
        │
        ▼
Render Relay
forward อย่างเดียว
        │
        ▼
Android
ตรวจ current entityId
        │
        ├─ stale -> reject
        │
        └─ valid
             │
             ▼
disconnect VoiceCraft NetPeer
             │
             ▼
entity ถูก destroy
             │
             ▼
unbind_result(success)
             │
             ▼
Endstone clear binding state
```

## ทำไมต้องมี entityId

สมมติผู้เล่นเคย bind entity `20` แล้ว client reconnect กลายเป็น entity `25`

ถ้า unbind request เก่าของ entity `20` มาถึงช้า ระบบต้องไม่ disconnect entity `25`

ดังนั้น Android จะตรวจ

```text
requested entityId == current bound entityId
```

ก่อนทำ destructive action ทุกครั้ง

## ทำไม Relay ห้าม replay unbind

`unbind` เป็น destructive control message

Relay จึงตั้งใจ **ไม่ cache และไม่ replay** message นี้

ถ้า Relay เก็บ unbind ไว้แล้วส่งซ้ำหลัง reconnect อาจทำให้ session ใหม่ถูก disconnect โดย request เก่าได้

---

# 8. Unexpected Disconnect และ Auto Rebind

Manual Unbind กับ Unexpected Disconnect ถูกแยกกัน

ถ้า VoiceCraft Client หลุดเอง เช่น

- แอป client ปิด
- network หลุด
- UDP/peer session จบ

แต่ Minecraft player ยังอยู่ใน server Android จะส่ง

```text
voice_client_disconnected
```

ไปยัง Endstone

Endstone จะ

1. ล้าง bound state เก่า
2. แจ้งผู้เล่น
3. รอประมาณ 5 วินาที
4. เปิด Bind flow ใหม่

แต่ถ้าเป็น **Manual Unbind** Android จะบันทึกว่า entity ถูก disconnect โดยคำสั่งของผู้เล่น และจะ suppress Auto Rebind event เพื่อไม่ให้หน้าต่าง Bind เด้งกลับมาทันที

---

# 9. Render Relay คืออะไร

Render Relay เป็น WebSocket control-plane relay ระหว่าง

```text
Endstone <-> Android
```

มันไม่ได้รู้วิธี decode หรือ route VoiceCraft audio

Relay มี endpoint หลัก

```text
/bridge   WebSocket
/health   HTTP health/status
```

## Authentication

เมื่อ WebSocket เชื่อมต่อ ฝั่ง Android และ Endstone ต้องส่ง `hello` ก่อน

ข้อมูลหลัก

```text
role = android | endstone
serverId
secret
protocol
```

Relay ตรวจ Bridge Secret แบบ timing-safe comparison

ถ้า hello ไม่มาภายในเวลาที่กำหนดหรือ secret ไม่ถูกต้อง connection จะถูกปิด

## Room isolation

แต่ละ `serverId` มี room ของตัวเอง

```text
room: mcsv-main
├─ android connection
├─ endstone connection
├─ latest player states
└─ pending binds
```

ดังนั้น server คนละ `serverId` จะไม่ควรเห็น state ของกันและกัน

ถ้ามี connection role เดียวกันเข้ามาใหม่ Relay จะใช้ connection ใหม่แทนของเดิม

---

# 10. Relay Cache และ Snapshot Recovery

Render Relay เก็บ cache เฉพาะข้อมูลที่ปลอดภัยต่อการ replay

## ข้อมูลที่ cache

```text
player_state ล่าสุดของแต่ละ player
pending bind requests
```

เมื่อ Android reconnect

```text
Android reconnect
      │
      ▼
sync_begin
      │
      ├─ replay latest player states
      ├─ replay pending binds
      │
      ▼
sync_end
      │
      ▼
request_snapshot -> Endstone
```

หลังจากนั้น Endstone จะส่ง snapshot ปัจจุบันเพื่อให้ Android reconcile state อีกครั้ง

## ข้อมูลที่ไม่ cache

```text
unbind
```

เพราะเป็น destructive action

Relay จำกัด pending bind cache และมี WebSocket ping/pong heartbeat เพื่อกำจัด connection ที่ตายแล้ว

---

# 11. Primary / Backup Relay Failover

Android และ Endstone รองรับ

```text
Primary Relay
Backup Relay #1
Backup Relay #2
...
```

ลำดับเป็นวงกลม

```text
Primary
   ↓
Backup #1
   ↓
Backup #2
   ↓
Primary
```

ค่าหลัก

```text
reconnect_seconds = 5
max_attempts = 5
peer_timeout_seconds = 30
```

## Relay จะถือว่าสุขภาพดีเมื่อ

ไม่ใช่แค่ WebSocket connect สำเร็จ

แต่ต้องเห็น peer อีกฝั่งด้วย

ตัวอย่าง Android

```text
Android -> Relay connected
แต่ Relay ไม่เห็น Endstone
```

ถ้าสถานะนี้นานเกิน `30s` จะถือว่า relay path ใช้งานไม่ได้

## Failover algorithm

```text
ลอง Primary ครั้งที่ 1
   ↓ fail
รอ 5s
   ↓
...
ลอง Primary ครั้งที่ 5
   ↓ fail
เปลี่ยนไป Backup #1
```

ถ้ามี Primary อย่างเดียว ระบบจะเริ่ม retry cycle ของ Primary ใหม่แทนที่จะหยุด VoiceCraft Server

## สิ่งที่สำคัญที่สุด

**Relay failover ไม่ restart VoiceCraft UDP Runtime**

ดังนั้น

```text
Control plane ขาดชั่วคราว
        │
        ├─ WSS reconnect/failover
        │
        └─ VoiceCraft UDP runtime ยังทำงาน
```

VoiceCraft client ที่เชื่อมอยู่ไม่ควรถูกบังคับให้ disconnect เพียงเพราะ Render Relay เปลี่ยนตัว

---

# 12. ข้อความแจ้งเตือนใน Minecraft

เมื่อ control plane มีปัญหา Endstone จะแจ้งผู้เล่นผ่าน Minecraft chat

ใช้ Bedrock formatting code `§` และไม่มี emoji

แนวทางสี

```text
แดง     ระบบ Relay/Control plane ใช้งานไม่ได้
เหลือง   กำลัง retry หรือย้ายไป Backup
เขียว    Backup เชื่อมสำเร็จ หรือ Primary กลับมาทำงาน
```

ระบบมี anti-spam state เพื่อไม่ส่งข้อความ outage ซ้ำทุก 5 วินาทีระหว่าง retry

---

# 13. Android UI ทำหน้าที่อะไร

UI ไม่ใช่ตัว VoiceCraft protocol เอง แต่เป็น control surface ของ Android service

หน้าหลักถูกแบ่งให้ใช้งานง่ายขึ้นเป็น

```text
ภาพรวม
Relay
Start / Stop
Logs
Settings
```

Settings รวมเมนู application-level เช่น

- ภาษา
- Theme
- Information / Guide
- Account
- Voice Port
- Server Key

Relay page ใช้ตั้ง

- Primary Render URL
- Backup Relay URLs
- Server ID
- Bridge Secret
- Plugin configuration

แอปจะแปลง Render service URL เช่น

```text
https://voicecraft-main.onrender.com
```

เป็น

```text
wss://voicecraft-main.onrender.com/bridge
```

สำหรับ Bridge connection

---

# 14. Security Model

## Bridge Secret

Primary และ Backup Relay ทุกตัวต้องใช้ Bridge Secret เดียวกับ Android และ Endstone ของ server นั้น

```text
Android
Endstone
Primary Relay
Backup Relay(s)
        │
        └── same Bridge Secret
```

Bridge Secret ไม่ควรถูก commit ลง public repository

## Guest local storage

Free Account ใช้

- Android Keystore
- AES-GCM
- `NoBackupFilesDir`
- Android backup exclusion

## Runtime logs

ค่าที่เป็น secret เช่น Server Key / Bridge Secret / Binding Key จะไม่ถูกเขียนแบบ plaintext ลง runtime log ตาม flow ปกติ

## Unbind safety

- ต้องมี `requestId`
- ต้องมี `entityId`
- Android ตรวจ entity ปัจจุบัน
- Relay ไม่ replay destructive unbind

---

# 15. ตารางเส้นทางข้อมูล

| Data | Source | Path | Destination | Transport |
|---|---|---|---|---|
| Voice audio | VoiceCraft Client | Direct | Android VoiceCraft Runtime | UDP / LiteNetLib |
| Player position | Minecraft | Endstone → Relay → Android | VoiceCraft Entity | WSS JSON |
| Rotation | Minecraft | Endstone → Relay → Android | VoiceCraft Entity | WSS JSON |
| Dimension | Minecraft | Endstone → Relay → Android | VoiceCraft Entity | WSS JSON |
| Bind request | `/vc` | Endstone → Relay → Android | Bridge Controller | WSS JSON |
| Bind result | Android | Android → Relay → Endstone | `/vc` state | WSS JSON |
| Unbind request | `/vc` | Endstone → Relay → Android | VoiceCraft NetPeer | WSS JSON |
| Relay health | Relay | peer_status | Android / Endstone | WSS JSON |
| Player snapshot | Endstone | Relay | Android | WSS JSON |

---

# 16. การติดตั้งโดยย่อ

## Render Relay

สร้าง Render Web Service จาก directory

```text
VoiceCraft.Bridge.Relay
```

ค่าหลัก

```text
Runtime: Node
Build Command: npm install --omit=dev
Start Command: npm start
Health Check Path: /health
```

Environment

```text
BRIDGE_SECRET=<strong secret อย่างน้อย 16 ตัวอักษร>
```

Backup Relay ใช้ source เดียวกันและ secret เดียวกัน

## Android

ตั้งค่า

```text
Primary Render Service URL
Backup Relay URLs (optional)
Server ID
Bridge Secret
Voice Port
Server Key
```

## Endstone

ติดตั้ง

```text
endstone_voicecraft-0.2.6-py3-none-any.whl
```

ลบ wheel รุ่นเก่าก่อน

Android สามารถสร้าง Plugin Config ที่พร้อมนำไปใช้ใน Endstone ได้

ตัวอย่าง bridge config

```toml
[bridge]
enabled = true
url = "wss://voicecraft-main.onrender.com/bridge"
backup_urls = [
  "wss://voicecraft-backup1.onrender.com/bridge"
]
server_id = "mcsv-main"
secret = "SAME_SECRET_AS_ANDROID_AND_RELAY"
reconnect_seconds = 5
max_attempts = 5
peer_timeout_seconds = 30
```

## VoiceCraft Client

สำหรับ LAN test

```text
<ANDROID_LAN_IP>:9050
```

และตั้ง

```text
Positioning Type = Server
```

จากนั้นใช้ Binding Key ที่ client แสดงกับ `/vc` ใน Minecraft

---

# 17. โครงสร้าง Repository

```text
VoiceCraft.Upstream/
└─ VoiceCraft v1.7.1 ต้นฉบับที่ pin commit ไว้

VoiceCraft.Server.Android/
├─ Android UI
├─ Foreground Service
├─ Endstone Bridge Controller
├─ Runtime diagnostics
└─ Server preferences

VoiceCraft.Guest.Patch/
├─ Account Gate
├─ Registered Account session
├─ Free/Guest Account storage
├─ Android Keystore encryption
└─ build-time patch script

VoiceCraft.Endstone/
├─ Endstone plugin
├─ /vc UI
├─ player tracking
├─ bind/unbind logic
└─ relay client

VoiceCraft.Bridge.Relay/
└─ Node.js WebSocket relay

supabase/
├─ Account/Guest backend components
└─ migrations/functions used by account layer

tools/
└─ Android compatibility/build patches
```

---

# 18. CI และ Release

Android build pipeline ทำงานโดยประมาณ

```text
Checkout repository
      │
      ▼
Checkout pinned VoiceCraft upstream
      │
      ▼
Apply Android headless patches
      │
      ▼
Apply McHttp Android compatibility
      │
      ▼
Apply Endstone bridge runtime
      │
      ▼
Apply Account / Guest patch
      │
      ▼
Source guards
      │
      ▼
.NET 10 Android ARM64 build
      │
      ▼
Signed APK artifact
```

Release workflow ยัง build Endstone wheel, ตรวจ Relay syntax และสร้าง `SHA256SUMS.txt`

---

# 19. ข้อจำกัดปัจจุบัน

Render Relay ในเวอร์ชันนี้เป็น **control plane เท่านั้น**

ดังนั้นสำหรับผู้เล่นนอก LAN หรือกรณี Android อยู่หลัง CGNAT เส้นทาง UDP ยังเป็นข้อจำกัดหลัก

```text
Control plane
Minecraft -> Render -> Android
ใช้งานผ่าน Internet ได้

Voice audio
Voice Client -> UDP -> Android
ยังต้องเข้าถึง Android UDP endpoint ได้
```

แนวทาง phase ถัดไปคือ Public UDP Relay / Tunnel

```text
VoiceCraft Client
      │ UDP
      ▼
Public UDP Relay / VPS
      │ authenticated tunnel
      ▼
Android Tunnel Agent
      │
      ▼
127.0.0.1:9050
      │
      ▼
VoiceCraft Runtime
```

Render WSS ควรยังคงเป็น control plane ไม่ควรนำ voice audio ไป tunnel ผ่าน Render WebSocket

---

# เครดิต VoiceCraft ต้นฉบับ

โปรเจกต์นี้สร้างต่อยอดจาก **VoiceCraft** ของ **AvionBlock** และไม่ได้อ้างว่า VoiceCraft protocol/runtime ต้นฉบับถูกสร้างโดยโปรเจกต์นี้

## Original Project

- **VoiceCraft by AvionBlock:** https://github.com/AvionBlock/VoiceCraft
- **Primary upstream development repository:** https://gitlab.avion.team/voicecraft/VoiceCraft
- **Official documentation:** https://docs.voicecraft.chat
- **GitHub contributors:** https://github.com/AvionBlock/VoiceCraft/graphs/contributors

VoiceCraft ต้นฉบับอธิบายตัวเองว่าเป็น proximity voice platform ที่ประกอบด้วย client, standalone server, Minecraft integrations และ API/transports หลายรูปแบบ โปรเจกต์ Server Mobile นี้ใช้ VoiceCraft v1.7.1 เป็น runtime หลัก แล้วเพิ่ม Android hosting, Endstone bridge, Render failover และ account layer รอบ runtime เดิม

## รายชื่อเครดิตจาก VoiceCraft ต้นฉบับ

รายชื่อด้านล่างอ้างอิงจาก `CreditsViewModel` ของ VoiceCraft upstream v1.7.1 โดยตรง

| Name | Role ใน VoiceCraft ต้นฉบับ |
|---|---|
| **SineVector241** | Author, Programmer |
| **Miniontoby** | Translator, Programmer |
| **Unny** | Translator |
| **AlphaMSq** | Translator, Programmer |
| **R JustGuyz** | Translator |

นอกจากนี้ GitHub release/history ของ VoiceCraft ยังมี contributors อื่น ๆ เช่น **lil-jon-crunk** และผู้มีส่วนร่วมในแต่ละ release จึงควรดู Contributors/commit history ของ upstream สำหรับรายชื่อที่อัปเดตครบที่สุด

ขอขอบคุณทีม VoiceCraft และ contributors ทุกคนที่สร้าง protocol, server runtime, client, network layer และระบบต่าง ๆ ที่เป็นรากฐานของโปรเจกต์นี้

---

# License / Attribution

VoiceCraft ต้นฉบับเผยแพร่ภายใต้ **GNU General Public License v3.0 (GPL-3.0)** โปรดตรวจสอบ license และเงื่อนไขของ upstream ก่อนนำ source หรือ binary ที่เกี่ยวข้องไปแจกจ่ายหรือแก้ไขต่อ

- Upstream license: https://github.com/AvionBlock/VoiceCraft/blob/master/LICENSE.md
- Upstream repository: https://github.com/AvionBlock/VoiceCraft

ส่วนที่พัฒนาต่อยอดใน repository นี้ควรถูกใช้งานโดยคำนึงถึง attribution และ license ของ VoiceCraft ต้นฉบับเสมอ
