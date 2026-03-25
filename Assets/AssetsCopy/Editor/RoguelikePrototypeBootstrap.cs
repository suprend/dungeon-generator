using System.Collections.Generic;
using System.IO;
using DanverPlayground.Roguelike.Characters;
using DanverPlayground.Roguelike.Characters.Abilities;
using DanverPlayground.Roguelike.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DanverPlayground.Editor
{
    // Editor-утилита, которая пересобирает весь прототип сцены и данные персонажей с нуля.
    public static class RoguelikePrototypeBootstrap
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string GeneratedArtFolder = "Assets/Art/Generated";
        private const string AbilityFolder = "Assets/Data/Abilities";
        private const string CharacterFolder = "Assets/Data/Characters";
        private const string CharacterPrefabFolder = "Assets/Prefabs/Characters";

        [MenuItem("Tools/DanverPlayground/Bootstrap Character Prototype")]
        public static void BootstrapFromMenu()
        {
            Bootstrap();
        }

        public static void Bootstrap()
        {
            // Подготавливаем структуру папок перед генерацией ассетов.
            EnsureFolder("Assets/Art");
            EnsureFolder(GeneratedArtFolder);
            EnsureFolder("Assets/Data");
            EnsureFolder(AbilityFolder);
            EnsureFolder(CharacterFolder);
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(CharacterPrefabFolder);

            DeleteObsoleteAssets();

            // Генерируем временные однотонные спрайты, чтобы механики можно было тестировать без готового арта.
            Sprite warriorSprite = CreateSolidSprite("Warrior", new Color32(207, 85, 73, 255));
            Sprite rangerSprite = CreateSolidSprite("Ranger", new Color32(85, 193, 112, 255));
            Sprite mageSprite = CreateSolidSprite("Mage", new Color32(88, 138, 255, 255));
            Sprite priestSprite = CreateSolidSprite("Priest", new Color32(236, 214, 130, 255));
            Sprite enemySprite = CreateSolidSprite("Enemy", new Color32(142, 78, 163, 255));
            Sprite arenaSprite = CreateSolidSprite("ArenaFloor", new Color32(53, 60, 70, 255));

            DashAbilityDefinition dash = CreateOrUpdateAbilityAsset<DashAbilityDefinition>("Ability_Dash.asset", so =>
            {
                so.FindProperty("abilityName").stringValue = "Dash";
                so.FindProperty("cooldown").floatValue = 3f;
                so.FindProperty("dashSpeed").floatValue = 15f;
                so.FindProperty("dashDuration").floatValue = 0.2f;
            });

            HealPulseAbilityDefinition healPulse = CreateOrUpdateAbilityAsset<HealPulseAbilityDefinition>("Ability_HealPulse.asset", so =>
            {
                so.FindProperty("abilityName").stringValue = "Heal Pulse";
                so.FindProperty("cooldown").floatValue = 8f;
                so.FindProperty("radius").floatValue = 4f;
                so.FindProperty("healAmount").floatValue = 2.5f;
            });

            SpeedBoostAbilityDefinition speedBoost = CreateOrUpdateAbilityAsset<SpeedBoostAbilityDefinition>("Ability_SpeedBoost.asset", so =>
            {
                so.FindProperty("abilityName").stringValue = "Haste";
                so.FindProperty("cooldown").floatValue = 6f;
                so.FindProperty("speedMultiplier").floatValue = 1.8f;
                so.FindProperty("duration").floatValue = 3.5f;
            });

            ShockwaveAbilityDefinition shockwave = CreateOrUpdateAbilityAsset<ShockwaveAbilityDefinition>("Ability_Shockwave.asset", so =>
            {
                so.FindProperty("abilityName").stringValue = "Shockwave";
                so.FindProperty("cooldown").floatValue = 5f;
                so.FindProperty("radius").floatValue = 3f;
                so.FindProperty("force").floatValue = 13f;
            });

            ProjectileBurstAbilityDefinition projectileBurst = CreateOrUpdateAbilityAsset<ProjectileBurstAbilityDefinition>("Ability_ProjectileBurst.asset", so =>
            {
                so.FindProperty("abilityName").stringValue = "Arcane Volley";
                so.FindProperty("cooldown").floatValue = 4.5f;
                so.FindProperty("projectileCount").intValue = 5;
                so.FindProperty("spreadAngle").floatValue = 34f;
                so.FindProperty("damageMultiplier").floatValue = 1.2f;
                so.FindProperty("speedMultiplier").floatValue = 1.15f;
            });

            // Создаём четыре игровых класса со своими статами, базовой стрельбой и наборами активных способностей.
            CharacterDefinition warrior = CreateOrUpdateCharacter(
                "Character_Warrior.asset",
                "warrior",
                "Warrior",
                warriorSprite,
                new Color32(255, 167, 132, 255),
                new ActiveAbilityDefinition[] { dash, shockwave, speedBoost },
                10f,
                4.8f,
                18f,
                24f,
                2f,
                1.4f,
                4f,
                11f,
                1.1f,
                0.28f);

            CharacterDefinition ranger = CreateOrUpdateCharacter(
                "Character_Ranger.asset",
                "ranger",
                "Ranger",
                rangerSprite,
                new Color32(159, 255, 171, 255),
                new ActiveAbilityDefinition[] { speedBoost, projectileBurst, dash },
                7f,
                6f,
                20f,
                28f,
                1f,
                1f,
                6.5f,
                14f,
                1.15f,
                0.22f);

            CharacterDefinition mage = CreateOrUpdateCharacter(
                "Character_Mage.asset",
                "mage",
                "Mage",
                mageSprite,
                new Color32(154, 198, 255, 255),
                new ActiveAbilityDefinition[] { projectileBurst, shockwave, speedBoost },
                6f,
                4.9f,
                15f,
                20f,
                1f,
                1.2f,
                5f,
                13f,
                1.25f,
                0.24f);

            CharacterDefinition priest = CreateOrUpdateCharacter(
                "Character_Priest.asset",
                "priest",
                "Priest",
                priestSprite,
                new Color32(255, 243, 173, 255),
                new ActiveAbilityDefinition[] { healPulse, speedBoost, projectileBurst },
                8f,
                4.6f,
                14f,
                18f,
                1f,
                0.9f,
                4.5f,
                12f,
                1.2f,
                0.24f);

            GameObject warriorPrefab = CreateOrUpdateCharacterPrefab("PF_Warrior.prefab", warrior, CharacterControlMode.Player, Color.white);
            GameObject rangerPrefab = CreateOrUpdateCharacterPrefab("PF_Ranger.prefab", ranger, CharacterControlMode.AI, Color.white);
            GameObject magePrefab = CreateOrUpdateCharacterPrefab("PF_Mage.prefab", mage, CharacterControlMode.AI, Color.white);
            GameObject priestPrefab = CreateOrUpdateCharacterPrefab("PF_Priest.prefab", priest, CharacterControlMode.AI, Color.white);

            // После генерации данных полностью пересобираем игровую сцену.
            SetupScene(arenaSprite, enemySprite, warriorPrefab, rangerPrefab, magePrefab, priestPrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("Roguelike combat prototype bootstrapped successfully.");
        }

        private static void SetupScene(Sprite arenaSprite, Sprite enemySprite, GameObject warriorPrefab, GameObject rangerPrefab, GameObject magePrefab, GameObject priestPrefab)
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // Очищаем всё, кроме стандартной камеры и света.
            foreach (GameObject rootObject in scene.GetRootGameObjects())
            {
                if (rootObject.name == "Main Camera" || rootObject.name == "Directional Light")
                {
                    continue;
                }

                Object.DestroyImmediate(rootObject);
            }

            Camera camera = Object.FindObjectOfType<Camera>();
            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                camera = cameraObject.AddComponent<Camera>();
                camera.tag = "MainCamera";
            }

            camera.orthographic = true;
            camera.orthographicSize = 5.5f;
            camera.backgroundColor = new Color32(28, 31, 38, 255);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.transform.position = new Vector3(0f, 0f, -10f);

            CameraFollow2D follow = camera.GetComponent<CameraFollow2D>();
            if (follow == null)
            {
                follow = camera.gameObject.AddComponent<CameraFollow2D>();
            }

            // Простая арена нужна только как фон для теста движения и боя.
            GameObject arena = new GameObject("Arena");
            SpriteRenderer arenaRenderer = arena.AddComponent<SpriteRenderer>();
            arenaRenderer.sprite = arenaSprite;
            arenaRenderer.sortingOrder = -10;
            arena.transform.position = new Vector3(0f, 0f, 1f);
            arena.transform.localScale = new Vector3(30f, 18f, 1f);

            GameObject partyRoot = new GameObject("Party");
            PartyController partyController = partyRoot.AddComponent<PartyController>();

            GameCharacter warrior = ((GameObject)PrefabUtility.InstantiatePrefab(warriorPrefab, scene)).GetComponent<GameCharacter>();
            GameCharacter ranger = ((GameObject)PrefabUtility.InstantiatePrefab(rangerPrefab, scene)).GetComponent<GameCharacter>();
            GameCharacter mage = ((GameObject)PrefabUtility.InstantiatePrefab(magePrefab, scene)).GetComponent<GameCharacter>();
            GameCharacter priest = ((GameObject)PrefabUtility.InstantiatePrefab(priestPrefab, scene)).GetComponent<GameCharacter>();

            warrior.name = "Warrior";
            ranger.name = "Ranger";
            mage.name = "Mage";
            priest.name = "Priest";

            warrior.transform.position = new Vector3(0f, 0f, 0f);
            ranger.transform.position = new Vector3(-1.8f, -1.35f, 0f);
            mage.transform.position = new Vector3(1.8f, -1.35f, 0f);
            priest.transform.position = new Vector3(0f, -2.5f, 0f);

            warrior.transform.SetParent(partyRoot.transform);
            ranger.transform.SetParent(partyRoot.transform);
            mage.transform.SetParent(partyRoot.transform);
            priest.transform.SetParent(partyRoot.transform);

            // На сцену сразу ставим несколько врагов для теста стрельбы, урона и ИИ-компаньонов.
            CreateEnemy(enemySprite, new Vector3(6f, 0f, 0f));
            CreateEnemy(enemySprite, new Vector3(8f, 2.5f, 0f));

            SerializedObject partySerialized = new SerializedObject(partyController);
            SerializedProperty charactersProperty = partySerialized.FindProperty("characters");
            charactersProperty.arraySize = 4;
            charactersProperty.GetArrayElementAtIndex(0).objectReferenceValue = warrior;
            charactersProperty.GetArrayElementAtIndex(1).objectReferenceValue = ranger;
            charactersProperty.GetArrayElementAtIndex(2).objectReferenceValue = mage;
            charactersProperty.GetArrayElementAtIndex(3).objectReferenceValue = priest;
            partySerialized.FindProperty("cameraFollow").objectReferenceValue = follow;
            partySerialized.FindProperty("previousCharacterKey").intValue = (int)KeyCode.Q;
            partySerialized.FindProperty("nextCharacterKey").intValue = (int)KeyCode.E;
            partySerialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static CharacterDefinition CreateOrUpdateCharacter(
            string fileName,
            string characterId,
            string displayName,
            Sprite sprite,
            Color projectileColor,
            IReadOnlyList<ActiveAbilityDefinition> activeAbilities,
            float maxHealth,
            float moveSpeed,
            float acceleration,
            float deceleration,
            float contactDamage,
            float projectileDamage,
            float fireRate,
            float projectileSpeed,
            float projectileLifetime,
            float projectileScale)
        {
            string path = Path.Combine(CharacterFolder, fileName).Replace("\\", "/");
            CharacterDefinition asset = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<CharacterDefinition>();
                AssetDatabase.CreateAsset(asset, path);
            }

            // Заполняем ScriptableObject целиком через SerializedObject, чтобы Unity корректно сохранила вложенные данные.
            SerializedObject serialized = new SerializedObject(asset);
            serialized.FindProperty("characterId").stringValue = characterId;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("sprite").objectReferenceValue = sprite;
            serialized.FindProperty("projectileColor").colorValue = projectileColor;

            SerializedProperty stats = serialized.FindProperty("baseStats");
            stats.FindPropertyRelative("maxHealth").floatValue = maxHealth;
            stats.FindPropertyRelative("moveSpeed").floatValue = moveSpeed;
            stats.FindPropertyRelative("acceleration").floatValue = acceleration;
            stats.FindPropertyRelative("deceleration").floatValue = deceleration;
            stats.FindPropertyRelative("contactDamage").floatValue = contactDamage;

            SerializedProperty rangedAttack = serialized.FindProperty("rangedAttack");
            rangedAttack.FindPropertyRelative("damage").floatValue = projectileDamage;
            rangedAttack.FindPropertyRelative("fireRate").floatValue = fireRate;
            rangedAttack.FindPropertyRelative("projectileSpeed").floatValue = projectileSpeed;
            rangedAttack.FindPropertyRelative("projectileLifetime").floatValue = projectileLifetime;
            rangedAttack.FindPropertyRelative("projectileScale").floatValue = projectileScale;

            SerializedProperty abilities = serialized.FindProperty("activeAbilities");
            abilities.arraySize = 3;
            for (int i = 0; i < 3; i++)
            {
                abilities.GetArrayElementAtIndex(i).objectReferenceValue = i < activeAbilities.Count ? activeAbilities[i] : null;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static T CreateOrUpdateAbilityAsset<T>(string fileName, System.Action<SerializedObject> configure) where T : ActiveAbilityDefinition
        {
            string path = Path.Combine(AbilityFolder, fileName).Replace("\\", "/");
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }

            // Так удобно централизованно пересоздавать параметры способностей при каждом bootstrap.
            SerializedObject serialized = new SerializedObject(asset);
            configure(serialized);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static GameObject CreateOrUpdateCharacterPrefab(string fileName, CharacterDefinition definition, CharacterControlMode controlMode, Color tint)
        {
            // Префабы собираются кодом, чтобы вся стартовая сцена могла генерироваться автоматически.
            GameObject root = new GameObject(definition.DisplayName);
            Rigidbody2D body = root.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            root.AddComponent<CircleCollider2D>().radius = 0.38f;

            PlayerCharacterBrain playerBrain = root.AddComponent<PlayerCharacterBrain>();
            CompanionAIController aiBrain = root.AddComponent<CompanionAIController>();
            GameCharacter character = root.AddComponent<GameCharacter>();

            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = definition.Sprite;
            renderer.color = tint;
            renderer.sortingOrder = 10;
            visual.transform.localScale = new Vector3(0.8f, 0.8f, 1f);

            SerializedObject serialized = new SerializedObject(character);
            serialized.FindProperty("definition").objectReferenceValue = definition;
            serialized.FindProperty("spriteRenderer").objectReferenceValue = renderer;
            serialized.FindProperty("body").objectReferenceValue = body;
            serialized.FindProperty("playerBrain").objectReferenceValue = playerBrain;
            serialized.FindProperty("aiBrain").objectReferenceValue = aiBrain;
            serialized.FindProperty("controlMode").enumValueIndex = (int)controlMode;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            string path = Path.Combine(CharacterPrefabFolder, fileName).Replace("\\", "/");
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static void CreateEnemy(Sprite enemySprite, Vector3 position)
        {
            // Для прототипа враг создаётся прямо в сцене без отдельного prefab.
            GameObject enemy = new GameObject("Enemy");
            enemy.transform.position = position;

            Rigidbody2D body = enemy.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            enemy.AddComponent<CircleCollider2D>().radius = 0.42f;
            enemy.AddComponent<EnemyUnit>();

            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(enemy.transform, false);
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = enemySprite;
            renderer.sortingOrder = 9;
            visual.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
        }

        private static Sprite CreateSolidSprite(string baseName, Color32 color)
        {
            string path = Path.Combine(GeneratedArtFolder, baseName + ".png").Replace("\\", "/");
            string absolutePath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, path);

            // Текстура создаётся на лету и импортируется как Sprite.
            Texture2D texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[64 * 64];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spritePixelsPerUnit = 64;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            // Рекурсивно создаём недостающие папки в проекте.
            string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            string folderName = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent ?? "Assets", folderName);
        }

        private static void DeleteObsoleteAssets()
        {
            // Удаляем устаревшие ассеты, которые не относятся к текущему составу прототипа.
            DeleteAssetsNotInKeepSet(
                CharacterFolder,
                new HashSet<string>
                {
                    "Character_Warrior.asset",
                    "Character_Ranger.asset",
                    "Character_Mage.asset",
                    "Character_Priest.asset"
                });

            DeleteAssetsNotInKeepSet(
                CharacterPrefabFolder,
                new HashSet<string>
                {
                    "PF_Warrior.prefab",
                    "PF_Ranger.prefab",
                    "PF_Mage.prefab",
                    "PF_Priest.prefab"
                });

            DeleteAssetsNotInKeepSet(
                GeneratedArtFolder,
                new HashSet<string>
                {
                    "Warrior.png",
                    "Ranger.png",
                    "Mage.png",
                    "Priest.png",
                    "Enemy.png",
                    "ArenaFloor.png"
                });
        }

        private static void DeleteAssetsNotInKeepSet(string folderPath, HashSet<string> keepFileNames)
        {
            string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (AssetDatabase.IsValidFolder(path))
                {
                    continue;
                }

                string fileName = Path.GetFileName(path);
                if (!keepFileNames.Contains(fileName))
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }
        }
    }
}
