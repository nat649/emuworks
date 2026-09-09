# EmuWorks — commencer ici

Émulateur **au niveau de la puce** : il exécute le vrai code machine ARM du
firmware NumWorks sur un STM32F730 émulé. Écran, clavier, scripts Python,
batterie — tout marche.

## Démarrer

```
C:\EmuWorks\EmuWorks.exe
```

Une seule application, rien à taper. Elle réunit ce qui était éparpillé dans huit
fichiers `.bat` et dans des commandes tapées à la main :

- choisir le firmware dans une liste ;
- ajouter, supprimer, ouvrir des scripts Python ;
- **voir l'écran de la calculatrice dans la fenêtre même**, au clavier du PC ;
- récupérer automatiquement son contenu à l'arrêt.

Aucune fenêtre Renode n'apparaît : il tourne sans interface, son écran est
rapatrié ici par une socket locale.

⚠️ **Le chemin ne doit contenir aucun espace.** Renode 1.16 ne sait pas les lire
(« Could not tokenize »). L'emplacement exact est libre : rien n'est codé en
dur. La copie posée sur le Bureau est une **archive**, pas un dossier
d'exécution — son chemin contient des espaces.

## Structure

```
C:\EmuWorks\
├── EmuWorks.exe      <- l'application
├── README.md         <- la page d'accueil du dépôt GitHub
├── LICENSE           <- MIT (ton code) ; Epsilon n'est PAS distribué
├── .gitignore        <- tient les firmwares hors du dépôt
│
├── rom\              <- LA CALCULATRICE
│   ├── internal.bin      flash interne  0x08000000
│   ├── external.bin      flash externe  0x90000000
│   └── scripts\*.py      tes scripts, en vrais fichiers texte
│
├── firmwares\        <- la bibliothèque de firmwares
│   ├── epsilon-15.5.0\             Epsilon officiel (démarre sur l'accueil)
│   ├── epsilon-15.5.0-assistant\   idem, avec l'assistant de langue
│   └── omega-2.0.2\                fork communautaire
│
├── renode\           <- l'émulateur
│   ├── numworks_n0110.repl   description matérielle du STM32F730
│   ├── NumWorks*.cs          écran ST7789, clavier 9×6, CRC32, ADC, mémoire
│   ├── numworks.resc         script de lancement
│   ├── tools\                outils Node (scripts, PNG, symboles ELF)
│   ├── build-firmware-n0110.yml   workflow GitHub de compilation
│   └── README.md             **la documentation complète**
│
├── app\              <- sources C# de l'application
├── ligne-de-commande\ <- les anciens .bat, toujours fonctionnels
└── web\              <- réplique web (aucune installation, mais pas de vrai firmware)
```

## Compiler son propre firmware

Possible et documenté — section **« Compiler ton propre firmware »** de
`renode\README.md` — mais aucun firmware modifié n'est livré ici : `firmwares\`
ne contient que des versions d'origine.

## Publier sur GitHub

Le dossier est déjà un dépôt git prêt à partir. `.gitignore` exclut tout ce qui
poserait un problème de droits : les firmwares (`*.bin`, `*.dfu`, `*.elf`,
`firmwares\`), les scripts d'exemple de la calculatrice, et les artefacts de
compilation. Epsilon est sous licence **CC BY-NC-SA 4.0** — clause
NonCommercial et partage à l'identique — donc on n'en redistribue aucun binaire ;
`renode\build-firmware-n0110.yml` permet à chacun de compiler le sien.

38 fichiers, aucun binaire. Pour publier :

```bash
git commit -m "Emulateur NumWorks N0110"
gh repo create emulateur-numworks --public --source=. --push
```

## Si quelque chose cloche

`renode\README.md` contient l'inventaire de ce qui est modélisé, la liste des
registres encore bouchonnés avec leur raison, et la méthode pour diagnostiquer un
firmware qui ne démarre pas.
