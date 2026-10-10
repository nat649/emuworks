# EmuWorks Core 0.1

Firmware ARM original pour le **materiel emule par EmuWorks**, sous la licence MIT du depot. Le code de demarrage, le calculateur, les pilotes et les glyphes sont ecrits dans ce dossier. Aucun code, image, police ou binaire Epsilon/Omega n'est utilise dans ce firmware.

L'interface utilise des cartes arrondies, un historique aere et des grands caracteres a traits adoucis. La police originale de `font.cjs` est rasterisee a la compilation : le firmware ne recalcule pas ses courbes pendant la saisie. Les petits libelles gardent leurs glyphes bitmap pour rester lisibles sur l'ecran 320 x 240.

## Utilisation

Dans EmuWorks, selectionner **emuworks-core-0.1**, cliquer **Installer**, puis **Demarrer**. Sur une installation neuve sans firmware, Core est installe automatiquement. Un firmware utilisateur deja present est conserve.

- `+ - * /`, nombres decimaux, parentheses, signes unaires et priorites usuelles.
- `^` : puissances **entieres de -100 a 100** ; les puissances s'associent a droite (`2^3^2 = 512`) et `-2^2 = -4`.
- `Entree` : calculer ; `Echap` : vider l'expression ; retour arriere : supprimer ; gauche/droite : deplacer le curseur.
- Haut/bas : rappeler les huit derniers calculs. Touche **A** : inserer `ANS`, le dernier resultat. Saisir par exemple `ANS*2` pour le reutiliser.
- Affichage de six chiffres significatifs, arithmetique flottante simple precision. Les arrondis et limites sont ceux d'une petite calculatrice numerique, sans calcul symbolique. Exposants decimaux saisis limites a -30..30, resultats limites a environ 1e30 en valeur absolue. Les tres petites valeurs peuvent s'arrondir a zero.
- Historique limite a la session ; il est efface au redemarrage. Pas de Python, de graphiques ni de fonctions trigonometriques dans cette version.

Le firmware tourne reellement sur le Cortex-M7 emule. Il utilise le bus d'ecran et la matrice GPIO du clavier, pas un calculateur cote Windows. Il tient dans la flash interne ; `external.bin` ne contient qu'une identification pour le format d'import de l'application.

**Ne pas flasher cette image sur une calculatrice physique** : le demarrage suppose les memoires et le bus d'ecran deja exposes par la plateforme Renode. Il ne configure pas l'ensemble du materiel reel.

## Construction

Installer Arm GNU Toolchain (`arm-none-eabi-gcc` et binutils), puis depuis la racine du depot :

```powershell
# Si le compilateur n'est pas dans PATH :
$env:ARM_GCC_BIN = 'C:\chemin\toolchain\bin'
node firmware/core/build.cjs
node tests/core.integration.cjs 'C:\Program Files\Renode\bin\Renode.exe'
```

Les produits temporaires vont dans `build/`. Pour mettre a jour les deux images originales embarquees : `node firmware/core/build.cjs --bundle`, puis recompiler l'application .NET. Les images dans `bundled/` sont les seuls binaires de firmware suivis dans Git. Elles permettent de compiler l'application sans installer un compilateur ARM.

Compilation initiale validee avec GCC Arm 12.2 MPACBTI-Rel1. Options principales : Cortex-M7, Thumb, FPU simple precision, `-ffreestanding -nostdlib -fno-builtin`. Aucun runtime C, libgcc ou bibliotheque mathematique n'est lie. Le compilateur est un outil externe, non distribue avec EmuWorks. Les sources sont fournies pour reconstruire et modifier le firmware.

## Tests

`tests.c` est une image de test distincte, non embarquee dans l'application. Elle execute 45 cas sur le processeur emule : priorites, parentheses, signes, puissance, erreurs et formatage. `tests/core.integration.cjs` verifie ensuite le clavier matriciel, les calculs, l'historique et genere une capture d'ecran dans un dossier temporaire.

Le nom EmuWorks et l'interface de Core identifient ce projet independant. Aucun logo NumWorks n'est embarque. Les eventuelles futures dependances devront etre examinees et accompagnees de leurs notices ; la licence MIT du projet ne change pas la licence d'un composant tiers.
