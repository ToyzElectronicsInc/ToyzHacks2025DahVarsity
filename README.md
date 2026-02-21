# ToyzHacks 2026 - DahVarsity Avatar Challenge
## MetaPerson SDK + Social Login + Nvidia ACE + Superpowers

---

## 🎯 HACKATHON CHALLENGE

**Build the complete avatar integration pipeline:**

1. **Social Login** - Facebook/Instagram authentication via PlayFab
2. **Avatar Creation** - MetaPerson SDK (photorealistic from selfie)
3. **Animation** - Nvidia ACE (lip-sync, facial expressions)
4. **Superpowers** - Unity VFX Graph (fireball, teleport, shields)
5. **Persistence** - PlayFab cloud storage

**Show us what you can build!** 🚀

---

## ⚠️ IMPORTANT: Ready Player Me DISCONTINUED

Ready Player Me has shut down. **Use MetaPerson SDK instead!**


---

## 🚀 QUICK START

### **1. Clone the Repo**

```bash
# Via GitHub Desktop (Recommended)
1. Open GitHub Desktop
2. File → Clone Repository
3. URL: https://github.com/ToyzElectronicsInc/ToyzHacks2025DahVarsity
4. Clone

# Via Command Line
git clone https://github.com/ToyzElectronicsInc/ToyzHacks2025DahVarsity.git
cd ToyzHacks2025DahVarsity
```

### **2. Install Unity**

**Version: Unity 2022.3.33f (LTS)**

```
1. Download Unity Hub
2. Unity Hub → Installs → Archives
3. Download 2022.3.33f from https://unity.com/releases/editor/archive
4. Add modules if needed (WebGL, Android, iOS)
```

### **3. Open Project**

```
1. Unity Hub → Projects → Add project from disk
2. Select ToyzHacks2025DahVarsity folder
3. Unity will import (5-10 min first time)
4. Open Scene: Assets/Scenes/DaGreatDeityDah.unity
```

### **4. Test Scene**

```
1. Press Play
2. WASD - Move
3. Mouse - Look around
4. Space - Jump
```

---

## 📚 PROJECT STRUCTURE

```
ToyzHacks2025DahVarsity/
├── Assets/
│   ├── Scenes/
│   │   └── DaGreatDeityDah.unity       # Main scene
│   ├── Scripts/
│   │   ├── Avatar/                     # YOUR CODE HERE
│   │   │   ├── MetaPersonLoader.cs     # (Create this)
│   │   │   ├── PlayFabAvatarSync.cs    # (Create this)
│   │   │   └── SuperpowerController.cs # (Create this)
│   │   └── ThirdPersonController/      # Character movement
│   ├── Prefabs/
│   │   └── PlayerCharacter.prefab
│   └── VFX/                            # Superpower effects
│       ├── FireballVFX.vfx             # (Create this)
│       ├── TeleportVFX.vfx             # (Create this)
│       └── ShieldVFX.vfx               # (Create this)
├── Packages/
│   └── manifest.json
├── Web/                                # (Create this folder)
│   ├── public/
│   └── src/
│       ├── components/
│       │   ├── FacebookLogin.tsx       # (Create this)
│       │   └── MetaPersonCreator.tsx   # (Create this)
│       └── App.tsx
└── README.md
```

---

## 🛠️ WHAT TO BUILD

### **Minimum Viable (2-3 hours):**

1. ✅ Facebook login working
2. ✅ MetaPerson iframe loads
3. ✅ Avatar created from selfie
4. ✅ Avatar URL saved to PlayFab
5. ✅ Avatar loads in Unity

### **Show Off Your Skills (add these):**

6. 🎯 **Nvidia ACE animation** (lip-sync from audio)
7. 🎯 **Superpowers with VFX** (fireball, teleport, shields)
8. 🎯 **S3 pipeline** for avatar storage
9. 🎯 **Polished UX** (loading states, error handling)
10. 🎯 **Creative abilities** (unique superpowers)

---

## 📋 SETUP ACCOUNTS

### **Required Accounts:**

1. **MetaPerson SDK**
   - Sign up: https://metaperson.avatarsdk.com/business.html
   - Get: Client ID + Client Secret
   - Free tier: 100 avatars/month

2. **Facebook Developer**
   - Sign up: https://developers.facebook.com
   - Create app (Type: Consumer)
   - Get: App ID + App Secret

3. **PlayFab**
   - Sign up: https://playfab.com
   - Create title
   - Get: Title ID
   - Enable Facebook in Add-ons

4. **Optional: Azure Account**

---

## 💻 CODE EXAMPLES

All complete code examples are in **Course 7**: https://dahvarsityai.com/courses/7

### **Quick Reference:**

**MetaPerson iframe:**
```html
<iframe 
    src="https://metaperson.avatarsdk.com/iframe.html"
    allow="fullscreen microphone camera"
    style="width:100%; height:600px;">
</iframe>
```

**Facebook Login:**
```javascript
PlayFabClient.LoginWithFacebook({
    TitleId: 'YOUR_TITLE_ID',
    AccessToken: fbToken,
    CreateAccount: true
}, onSuccess, onError);
```

**Unity GLB Loading:**
```csharp
var gltf = gameObject.AddComponent<GltfAsset>();
await gltf.Load(avatarUrl);
```

**Superpowers:**
```csharp
public class SuperpowerController : MonoBehaviour {
    void CastFireball() { /* See Course 7 */ }
    void Teleport() { /* See Course 7 */ }
    void ToggleShield() { /* See Course 7 */ }
}
```

**Full implementations in Course 7!**

---

## 🎨 UNITY VFX SETUP

### **1. Install VFX Graph**

```
Window → Package Manager → Visual Effect Graph → Install
```

### **2. Create Effects**

- **FireballVFX**: Orange/red particles with trail
- **TeleportVFX**: Blue/purple burst effect
- **ShieldVFX**: Semi-transparent sphere with pulse

**Step-by-step in Course 7!**

---

## 🧪 TESTING CHECKLIST

- [ ] Facebook login works
- [ ] MetaPerson iframe loads
- [ ] Avatar created from selfie
- [ ] Avatar URL saved to PlayFab
- [ ] Avatar loads in Unity scene
- [ ] Superpowers work (1, 2, 3 keys)
- [ ] VFX effects display correctly
- [ ] Avatar persists (logout → login → avatar loads)
- [ ] Nvidia ACE animation working (if implemented)

---

## 📤 SUBMISSION

### **What to Submit:**

1. **Code** - Fork repo, create feature branch, make PR
2. **Demo Video** (2-3 min) showing:
   - Login with Facebook ✓
   - Create avatar with MetaPerson ✓
   - Avatar loads in Unity ✓
   - Demonstrate superpowers ✓
   - Show Nvidia ACE animation (if implemented) ✓
3. **Screenshots** of key features
4. **Brief explanation** (how you implemented it)

### **Evaluation Criteria:**

- ✅ **Functionality** (40%) - Complete pipeline working?
- ✅ **Completeness** (20%) - All components integrated?
- ✅ **Creativity** (20%) - Unique superpowers/VFX?
- ✅ **Polish** (20%) - UX, error handling, presentation?

---

## 📚 DOCUMENTATION

### **Primary Resources:**

- **Course 7** (Complete Guide): https://dahvarsityai.com/courses/7
- **MetaPerson SDK**: https://docs.metaperson.avatarsdk.com/
- **PlayFab**: https://learn.microsoft.com/en-us/gaming/playfab/
- **Facebook Login**: https://developers.facebook.com/docs/facebook-login/

### **Unity Resources:**

- **glTFast** (GLB loader): https://github.com/atteneder/glTFast
- **VFX Graph**: https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@latest
- **Input System**: https://docs.unity3d.com/Packages/com.unity.inputsystem@latest

### **Video Tutorials:**

- MetaPerson Unity: https://www.youtube.com/watch?v=P5GNQENZSvk
- PlayFab Auth: https://www.youtube.com/watch?v=bu_bTnVuU4M
- Sample Work: https://www.youtube.com/playlist?list=PLVXRK2vU0EKu52dbmyFVqt6s4GddzuR5E

---

## 🐛 TROUBLESHOOTING

### **Unity won't open project**

```
Solution:
1. Verify Unity 2022.3.33f installed
2. Delete Library/ folder in project root
3. Reopen via Unity Hub
```

### **MetaPerson iframe won't load**

```
Solution:
1. Ensure HTTPS (required for camera access)
2. Check browser console for errors
3. Verify credentials (Client ID/Secret)
4. Add domain to MetaPerson account settings
```

### **Facebook login fails**

```
Solution:
1. Verify Facebook App ID matches PlayFab settings
2. Check OAuth redirect URIs configured correctly
3. Use Development Mode for testing
4. Regenerate access token if expired
```

### **Avatar won't load in Unity**

```
Solution:
1. Install glTFast package via Package Manager
2. Verify avatar URL format (must be *.glb)
3. Test URL in browser (should download GLB file)
4. Check Unity console for specific error messages
5. Ensure CORS headers allow Unity domain
```

### **Superpowers not working**

```
Solution:
1. Verify Input System package installed
2. Check VFX prefabs assigned in Inspector
3. Enable Input System in Player Settings
4. Ensure SuperpowerController script attached to avatar
```

**More troubleshooting in Course 7 FAQs (30+ Q&A)**

---

## 🏆 TIPS FOR SUCCESS

### **Time Management:**

- **Hour 1:** Setup accounts, install Unity, test scene
- **Hour 2:** Facebook login + MetaPerson integration
- **Hour 3:** PlayFab persistence
- **Hour 4:** Unity avatar loading
- **Hour 5+:** Superpowers, VFX, ACE, polish

### **Priority Order:**

1. **P0 (Must Have):** Login → Avatar → Save → Load
2. **P1 (Should Have):** Superpowers + VFX
3. **P2 (Nice to Have):** ACE animation, S3, advanced features

### **Getting Unstuck:**

- Read Course 7 FAQs first (most answers there!)
- Ask in Discord/Slack hackathon channel
- Review documentation links above
- Check example code in Course 7
- **Don't spend >30 min stuck** - ask for help or pivot!

### **Demo Video Tips:**

- Show complete flow (login → avatar → superpowers)
- Narrate what you're demonstrating
- Keep it 2-3 minutes (concise!)
- Show your unique creative additions
- Highlight technical challenges you solved

---

## 🎉 LET'S BUILD!

**You've got this!** Show us what you can create with:
- ✨ MetaPerson's photorealistic avatars
- 🔐 Facebook social login
- 🎬 Nvidia ACE animation
- ⚡ Your creative superpowers

**Questions?** 
- Check Course 7: https://dahvarsityai.com/courses/7
- Ask in hackathon Discord/Slack
- Review documentation above

**Good luck!** 🚀

---

## 📜 LICENSE

MIT License


## 🔗 IMPORTANT LINKS

- **Hackathon Registration**: https://dahvarsityai.com/register-step-one
- **Course 7** (Full Guide): https://dahvarsityai.com/courses/7
- **DahVarsity Website**: https://dahvarsityai.com
- **GitHub Repo**: https://github.com/ToyzElectronicsInc/ToyzHacks2025DahVarsity
- **Getting Started Doc**: https://docs.google.com/document/d/1AyO5ov3uEJcE3QvfP1LDtrQVz9FiN5Lt3u1E8HzXH2Y

---

**Ready to build the future of avatar-driven education? Let's go!** 