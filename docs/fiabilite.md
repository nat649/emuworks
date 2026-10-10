# Sauvegardes, import et fermeture

**EmuWorks Core 0.1** est le firmware original intégré. Son historique dure la
session et il ne possède pas Python : les sauvegardes de scripts décrites ici
concernent Epsilon/Omega compatibles. Passer à Core ne supprime pas les scripts.

## Dans l'application

- **Importer un firmware…** : choisir l'image interne puis l'image externe du même build N0110. Les noms `epsilon.internal.bin` et `epsilon.external.bin` sont acceptés. Les fichiers DFU/ELF doivent d'abord être extraits. L'import contrôle les tailles et la table de démarrage ARM ; cela ne garantit pas la compatibilité de tous les firmwares. Aucun firmware n'est téléchargé ou distribué par l'application.
- **Restaurer une sauvegarde…** : choisir le `manifest.json` d'une version dans `rom/sauvegardes/<date>/`. L'application vérifie les empreintes SHA-256, sauvegarde les scripts actuels puis restaure la version choisie. Redémarrer la calculatrice pour la charger.
- **Annuler le démarrage** fonctionne pendant la connexion. Fermer la fenêtre attend l'arrêt et la sauvegarde. Une perte de connexion déclenche également l'arrêt ; le journal explique l'erreur.

Les versions sont conservées avant chaque démarrage, import depuis la calculatrice, restauration et ajout/suppression depuis l'application. Elles ne sont pas purgées automatiquement. Supprimer manuellement les versions devenues inutiles lorsque la calculatrice est arrêtée.

## Ce qui est sauvegardé

L'historique contient les **scripts Python**, pas une image complète de la calculatrice : l'application redémarre le firmware à froid. Les réglages, fonctions et autres données ne font pas partie de cet historique. Les records non Python présents dans l'image de référence sont préservés lors de l'injection.

Chaque session possède un vidage indépendant dans `rom/.sessions/`. Un ancien `rom/sram.bin` ne peut donc pas remplacer les scripts après un démarrage raté. À l'arrêt normal, la machine est mise en pause avant le dernier vidage. Si Renode doit être terminé de force, la dernière sauvegarde périodique peut avoir quelques secondes de retard. Les vidages sont conservés pour diagnostic/récupération et ne sont pas purgés automatiquement.

Les écritures de fichiers passent par un temporaire puis un remplacement. Le remplacement du dossier de scripts conserve aussi un dossier de secours pour reprendre après une interruption. Un vidage invalide ou un stockage Python vide inattendu est refusé : consulter le journal. Pour exporter volontairement un stockage vide, utiliser `node renode/tools/rom.js pull <vidage> rom --force` après avoir arrêté l'application.

Un verrou empêche deux instances de l'application de modifier la même ROM. Les outils en ligne de commande et les éditeurs externes ne prennent pas ce verrou : les utiliser lorsque la calculatrice est arrêtée.

L'installation d'une paire de firmware conserve un journal de reprise : une installation interrompue est annulée au prochain lancement. L'application ne prétend pas détecter deux images individuellement valides provenant de builds différents ; sélectionner les deux fichiers du même artefact.

## Vérifications locales

Depuis la racine du dépôt, avec Node.js et le SDK .NET 8 ou ultérieur :

```powershell
node --test tests/storage.test.cjs
dotnet run --project tests/EmuWorks.Tests.csproj -- .
```

Les tests Windows utilisent un faux Renode, des données temporaires et le port local 3555 : fermer les émulateurs avant de les lancer. Ils vérifient l'import, la reprise après interruption, l'annulation, les redémarrages, la déconnexion et l'arrêt forcé. Aucun firmware n'est nécessaire.

Test facultatif avec Renode installé et une paire de firmware fournie séparément :

```powershell
node tests/renode.integration.cjs 'C:\Program Files\Renode\bin\Renode.exe' 'C:\chemin\firmware'
```

Ce test copie les données dans un dossier temporaire sans espace, vérifie le démarrage, une trame complète et un aller-retour de script. Le dossier et son journal sont conservés pour inspection.
