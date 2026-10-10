# EmuWorks — commencer ici

Lance `EmuWorks.exe` depuis un dossier sans espace, par exemple `C:\NumWorks`.
Renode reste invisible ; l’écran et les commandes sont réunis dans l’application.

## Firmware libre intégré

**EmuWorks Core 0.1** est inclus dans l’exécutable, avec ses sources sous MIT.
Sur une installation neuve, il est installé automatiquement. Si tu utilises déjà
Epsilon ou Omega, sélectionne **emuworks-core-0.1**, puis **Installer** et **Démarrer**.
Tes scripts Python restent dans leur dossier lorsque tu utilises Core.

Cette première version propose les quatre opérations, décimales, parenthèses,
puissances entières, ANS et huit calculs d’historique. Elle affiche six chiffres
significatifs. L’historique est remis à zéro au redémarrage. Python et les
fonctions scientifiques avancées ne sont pas encore inclus.

- **Entrée** : calculer.
- **Échap** : vider la saisie ; retour arrière : supprimer un caractère.
- **Gauche/droite** : déplacer le curseur.
- **Haut/bas** : rappeler un calcul.
- **A** : insérer ANS pour réutiliser le dernier résultat.

Renode et .NET Desktop Runtime 8 restent nécessaires. Node.js n’est pas nécessaire
pour Core ; il sert à la synchronisation Python des firmwares externes.

## Epsilon ou Omega, en option

Le bouton **Importer un firmware** demande les deux images binaires d’un même
build N0110 : l’interne puis l’externe. Les firmwares tiers ne sont pas distribués
avec le dépôt. Tu peux revenir à l’un d’eux via la bibliothèque de firmwares.

Avec un firmware compatible Python, le bouton **Restaurer une sauvegarde** permet
de choisir une version dans `rom/sauvegardes/`. Voir [les sauvegardes](docs/fiabilite.md).

## Sources et publication

Les sources et les deux petits binaires **originaux** de Core sont dans
[firmware/core](firmware/core/README.md). L’application les embarque à la compilation.
Le dépôt exclut toujours les firmwares tiers et les données de ta calculatrice.
La licence MIT est fournie dans `LICENSE` ; les éventuels composants tiers
conservent leurs propres licences.

Documentation matérielle et diagnostic : [renode/README.md](renode/README.md).
