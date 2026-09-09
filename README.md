# Émulateur NumWorks N0110

Un émulateur **au niveau du microcontrôleur** : il exécute le vrai code machine
ARM Cortex-M7 du firmware NumWorks sur un STM32F730 émulé, avec l'écran
ST7789V sur bus FMC, la matrice clavier 9×6, l'ADC de la batterie et l'unité
CRC32. Pas une réimplémentation de l'interface — le firmware d'origine, tel
qu'il tourne sur la calculatrice.

Le tout dans une seule fenêtre : Renode est lancé sans interface, son écran est
rapatrié dans l'application par une socket locale.

---

## ⚠️ Ce dépôt ne contient aucun firmware

Epsilon est publié par NumWorks sous licence **Creative Commons BY-NC-SA 4.0**
(attribution, pas d'usage commercial, partage à l'identique). Redistribuer ses
binaires imposerait ces conditions à tout ce dépôt, alors on n'en distribue
aucun — ni Epsilon, ni Omega, ni les scripts d'exemple livrés avec la
calculatrice.

**Tu compiles le tien**, depuis les sources officielles, avec le workflow
GitHub fourni : `renode/build-firmware-n0110.yml`. Deux minutes, et il produit
exactement les deux images que l'émulateur charge.

Le code de ce dépôt — modèles de périphériques, outils, application — est du
travail original sous licence MIT. Il décrit le **matériel** (registres du
STM32F730, protocole de la dalle, câblage du clavier), pas le firmware.

---

## Prérequis

| | |
|---|---|
| [Renode](https://renode.io) 1.16 | `winget install Renode.Renode` |
| [Node.js](https://nodejs.org) | `winget install OpenJS.NodeJS.LTS` — sert à la synchronisation des scripts |
| .NET Desktop Runtime 8 | pour l'application ; ou `dotnet publish` depuis `app/` |

> ⚠️ **Le dossier doit être installé sous un chemin sans espaces**, typiquement
> `C:\NumWorks\`. Renode 1.16 échoue silencieusement (« Could not tokenize »)
> sur un chemin qui en contient — c'est le premier piège du projet.

## Obtenir un firmware

1. Fork [numworks/epsilon](https://github.com/numworks/epsilon).
2. Copie `renode/build-firmware-n0110.yml` dans `.github/workflows/` sur la
   branche par défaut de ton fork.
3. Actions → **Firmware N0110 (Renode)** → Run workflow.
4. Récupère l'artifact et pose les deux images dans
   `firmwares/<nom>/internal.bin` et `external.bin`.

L'artifact contient deux variantes :

| | |
|---|---|
| `epsilon.internal.bin` / `epsilon.external.bin` | démarre directement sur l'accueil |
| `epsilon.onboarding.*` | avec l'assistant de première utilisation |

La différence est une cible de compilation (`make epsilon.dfu` contre
`epsilon.onboarding.dfu`), pas un patch. La première est préférable ici :
l'émulateur repart toujours d'un démarrage à froid, donc l'assistant de langue
réapparaîtrait à chaque lancement.

Un fork n'héritant pas des tags, le workflow prend un champ `repository` en
plus de `ref` : laisse `numworks/epsilon` pour compiler l'amont, ou mets ton
fork et ta branche pour compiler tes propres modifications.

## Utilisation

Lance `NumWorks.exe`. Une seule fenêtre :

- choix du firmware parmi ceux posés dans `firmwares/` ;
- gestion des scripts Python : ajouter, supprimer, ouvrir dans ton éditeur ;
- l'écran de la calculatrice, au clavier de ton PC ;
- enregistrement automatique à l'arrêt.

### Le dossier `rom/` **est** la calculatrice

```
rom/
├── internal.bin      flash interne  → 0x08000000
├── external.bin      flash externe  → 0x90000000
└── scripts/*.py      les scripts Python, en vrais fichiers texte
```

Au démarrage, les `.py` du dossier sont injectés dans la calculatrice ; à
l'arrêt, ce qu'elle contient est réécrit dans le dossier. **Le dossier fait
autorité au démarrage, la calculatrice fait autorité à la fin.** Tu peux donc
éditer tes scripts dans ton éditeur, les versionner, les partager — ce que
l'USB de la vraie calculatrice ne permet pas.

L'adresse du stockage est retrouvée à chaque démarrage en cherchant le magic
`0xEE0BDDBA` dans la SRAM : elle diffère d'un firmware à l'autre, et le dossier
suit sans rien reconfigurer.

## Comment ça marche

```
NumWorks.exe ──stdin──▶ Renode (sans interface)
     ▲                      │
     │                      ├── numworks_n0110.repl   la carte
     └──socket 3555─────────┤   NumWorksDisplay.cs    ST7789V sur bus FMC
        trame RGB565        │   NumWorksKeyboard.cs   matrice 9×6
                            │   NumWorksAdc.cs        batterie
                            │   NumWorksCrc.cs        CRC32 matériel
                            └── MemFile.cs            mémoire ↔ fichiers
```

Renode ouvre normalement ses propres fenêtres. Ici il tourne avec
`--disable-xwt`, piloté par son entrée standard : les touches deviennent des
commandes du moniteur, et `NumWorksDisplay` sert le framebuffer sur une socket
locale — un octet de requête, une trame 320×240 RGB565 en réponse.

La documentation technique complète — registres encore bouchonnés, méthode de
diagnostic d'un firmware qui ne démarre pas, les deux orientations de l'écran —
est dans [`renode/README.md`](renode/README.md).

## Licence

MIT, voir [LICENSE](LICENSE). Ne couvre ni Epsilon (© NumWorks, CC BY-NC-SA
4.0) ni Renode (© Antmicro, MIT), qui ne sont pas distribués ici.
