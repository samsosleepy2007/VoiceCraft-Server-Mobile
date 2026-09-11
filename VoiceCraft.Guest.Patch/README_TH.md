# VoiceCraft Local Guest / Free Account Patch

สิ่งที่แพตช์นี้ทำ:

- Free/Guest ไม่มี row ในฐานข้อมูล account
- Guest ID เป็น random `GUEST-...`
- Guest profile อยู่ใน `NoBackupFilesDir`
- profile/session เข้ารหัส AES-GCM
- key อยู่ใน Android Keystore
- ปิด cloud backup และ device-transfer restore ของข้อมูลแอป
- Clear App Data / Uninstall = Guest หาย
- Update APK / ปิดเปิดแอป = Guest เดิม
- Login บัญชีจริงใช้ Account API ที่ Admin เป็นคนออกบัญชีให้
- ไม่มี Register/Gmail/Email/Forgot Password
- Session บัญชีจริงเก็บแบบ encrypted
- มี Account Center สำหรับสลับ Free ↔ Registered

## Apply

```bash
python VoiceCraft.Guest.Patch/apply_guest_account_patch.py .
```

จากนั้น build Android project ตาม workflow ของ branch `account-v2-guest-local`

## Test matrix

1. Fresh install → LOGIN WITH ACCOUNT / CREATE FREE ACCOUNT
2. CREATE FREE → ได้ `GUEST-...`
3. Force close/open → Guest ID เดิม
4. Update APK → Guest ID เดิม
5. Clear App Data → Guest ID ใหม่
6. Uninstall/reinstall → Guest ID ใหม่
7. Supabase DB → ไม่มี Guest row ใน `voicecraft.accounts`
8. Normal/Premium login → ใช้ account ที่ Admin สร้างได้
9. Reset password → server/config/secrets ของ account เดิมยังอยู่
10. Patch APK เปลี่ยนข้อความ role → backend ยังเป็นผู้ตัดสิน role จริง

Backend `voicecraft-guest` แยกจาก `voicecraft-account` เพื่อไม่กระทบ Account API production.
