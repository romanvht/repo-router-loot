# Router Loot

Роутеры как ценные предметы в R.E.P.O.

[Скачать на Thunderstore](https://thunderstore.io/c/repo/p/romanvht/RouterLoot/)

## Установка

Зависимости: BepInEx 5 и REPOLib 4.2.0.

Импортируйте `RouterLoot-*.zip` в менеджер модов или скопируйте `RouterLoot.dll` и `routerloot.repobundle` в одну папку внутри `BepInEx/plugins`.

## Настройки

Конфиг: `BepInEx/config/romanvht.RouterLoot.cfg`.
В разделе каждого предмета доступны `ValueMin`, `ValueMax`, `Mass` и `Fragility` (0–100).

`Debug → EnableSpawnKey`: **F8** создаёт набор роутеров перед хостом во время уровня.

## Разработка

Unity **2022.3.62f3**, .NET SDK 9, PowerShell 7, Git, установленная R.E.P.O. и профиль с зависимостями мода.

Подготовка проекта:

```powershell
.\scripts\setup-unity.ps1 -GameDir 'D:\SteamLibrary\steamapps\common\REPO' -ProfileDir 'C:\path\to\profile'
```

Откройте `unity` в Unity Hub.

## Сборка

В Unity: `Router Loot → Build Mod`. Собирает DLL, AssetBundle и ZIP; результат открывается в проводнике, лог доступен в Console.

Из консоли: `.\scripts\build-unity.ps1 -UseLocalDependencies`

Результат: `dist/RouterLoot.dll`, `dist/routerloot.repobundle`, `dist/RouterLoot-*.zip`.

`-GameDir`, `-ProfileDir` и `-UnityEditor` задают пути, `-NoPackage` пропускает ZIP.

`Router Loot → Build Assets` в Unity собирает и проверяет AssetBundle в `obj/unity-bundle`.
