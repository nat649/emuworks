# EmuWorks Core integre

Les sources du firmware sont dans [nat649/emuworks-core](https://github.com/nat649/emuworks-core). Ce dossier conserve uniquement les deux images originales compilees dans `bundled/`, leur [licence MIT](LICENSE) et leur provenance dans [source.json](source.json). L'application les embarque toujours dans son executable ; aucun telechargement supplementaire n'est necessaire au demarrage.

## Mettre a jour

1. Cloner le depot Core a cote du depot EmuWorks.
2. Dans Core, executer `node build.cjs --bundle` avec Arm GNU Toolchain, puis committer les sources et les images.
3. Dans EmuWorks, executer `node tools/update-core.cjs ../emuworks-core`. Le depot Core doit etre propre. Le script importe les images du commit, la licence et leurs SHA-256.
4. Recompiler l'application avec `dotnet publish app/EmuWorks.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true`.
5. Committer les nouvelles images et `source.json` dans EmuWorks.

## Tester le firmware

Apres compilation de Core, depuis EmuWorks :

```powershell
node tests/core.integration.cjs 'C:\Program Files\Renode\bin\Renode.exe' ../emuworks-core
```

Le test utilise les peripheriques Renode de l'emulateur et les images de test construites dans le depot Core. Il verifie les calculs, le clavier, l'historique et le rafraichissement partiel. Aucun firmware tiers n'est necessaire.
