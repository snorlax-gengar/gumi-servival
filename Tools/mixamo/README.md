# Mixamo 캐릭터·동작 (저장소에 포함하지 않음)

Mixamo(https://www.mixamo.com, 어도비 무료 계정) 파일은 약관상 원본을 공개 저장소에 올릴 수 없어서 `.gitignore`로 뺐다.
새 PC에서는 아래 파일을 받아 `Assets/Resources/Mixamo/`에 이 이름으로 넣는다. 없으면 게임은 기존 Quaternius 모델로 돌아간다.

공통: Format = FBX for Unity, 30fps. 캐릭터는 With Skin, 동작은 Without Skin.

| 파일 이름 | Mixamo에서 |
|---|---|
| Player.fbx | 캐릭터 Remy (With Skin, 아무 동작) |
| Android.fbx | 캐릭터 X Bot (With Skin, 아무 동작) |
| Idle.fbx | Breathing Idle |
| Walk.fbx | Walking (In Place 권장) |
| Run.fbx | Running (In Place 권장) |
| CrouchWalk.fbx | Crouched Walking (In Place 권장) |
| AttackMelee.fbx | Standing Melee Attack Horizontal |
| AttackBat.fbx | Baseball Hit |
| Dying.fbx | Dying |
| MutantPunch.fbx | Mutant Punch |
| HitReaction.fbx | Hit Reaction (선택) |

## 텍스처 꺼내기 (필수)
Remy처럼 텍스처가 FBX 안에 묻힌 캐릭터는 Unity가 텍스처를 못 찾아 흰 마네킹처럼 보인다. 넣은 뒤 한 번 실행:
```
python Tools/mixamo/extract_textures.py Assets/Resources/Mixamo/Player.fbx Assets/Resources/Mixamo/Tex
```
