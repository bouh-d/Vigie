# Vigie

Vigie garde un ordinateur portable Windows éveillé pendant qu'une tâche Claude
Code s'exécute, y compris capot fermé. Elle rétablit chaque réglage
d'alimentation qu'elle modifie à la fin de la session, même après un plantage ou
un arrêt forcé.

> English version: [README.md](README.md).

<!-- Une capture d'écran réelle est ajoutée ici une fois l'application exécutée
     sous Windows ; elle ne peut pas être prise depuis l'hôte de compilation Linux. -->

## Ce que Vigie ne peut pas faire

- faire tourner Claude Code pendant une veille, une veille prolongée ou un arrêt ;
- empêcher le bouton d'alimentation, une batterie critique, un arrêt thermique ou
  un redémarrage de Windows Update ;
- contourner un micrologiciel qui force la veille à la fermeture du capot (rare) ;
- garder le réseau si le portable sort de la zone Wi-Fi ;
- empêcher la surchauffe dans un sac ou un espace fermé.

Si le PC a dormi malgré tout, Vigie le détecte et l'écrit en clair (heure, durée,
impact sur Claude Code) au lieu d'afficher « En cours ».

## Prérequis

- Windows 10 version 1809 ou plus récent, ou Windows 11 (x64 ou ARM64).
- [Claude Code](https://code.claude.com/docs/en/setup) installé et connecté.
  Vigie cherche l'emplacement de l'installeur natif
  (`%USERPROFILE%\.local\bin\claude.exe`) et les exécutables du `PATH`.

## Installation

### Depuis une release

1. Téléchargez l'archive correspondant à votre architecture sur la
   [page des releases](https://github.com/bouh-d/vigie/releases).
2. Vérifiez son empreinte avec `SHA256SUMS.txt` :

   ```powershell
   Get-FileHash .\Vigie-win-x64.zip -Algorithm SHA256
   ```

3. Vigie n'est pas signée : Windows SmartScreen affiche un avertissement au
   premier lancement. Après avoir vérifié l'empreinte, choisissez
   **Informations complémentaires**, puis **Exécuter quand même**.
4. Décompressez l'archive et lancez `Vigie.exe`. Aucune installation ni droits
   administrateur ne sont nécessaires.

### Depuis les sources

```powershell
dotnet publish src/Vigie.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Remplacez `win-x64` par `win-arm64` pour l'ARM64. La compilation nécessite le
SDK .NET 10.

## Utilisation

1. Choisissez une durée (15 minutes, 1, 2 ou 4 heures, une valeur personnalisée,
   ou jusqu'à la fin de la tâche Claude Code).
2. Laissez **Fonctionner capot fermé** coché pour continuer à la fermeture du
   capot.
3. Au besoin, renseignez un dossier de projet et un prompt, choisissez
   **Tâche autonome**, et lancez Claude Code depuis Vigie.
4. Appuyez sur **Démarrer**. Fermez le capot et éloignez-vous.

La tâche autonome est le mode recommandé capot fermé : une session interactive
peut s'arrêter pour demander une autorisation, sans personne pour répondre.

## Ce que Vigie modifie, et comment elle le rétablit

Pendant une session, Vigie modifie au plus trois valeurs du mode d'alimentation,
note les valeurs d'origine dans `%LOCALAPPDATA%\Vigie\restore.json` avant toute
écriture, et les remet à l'identique à la fin :

- l'**action à la fermeture du capot**, sur secteur et sur batterie, passe à
  *Ne rien faire* (uniquement si « Fonctionner capot fermé » est coché et
  qu'aucune stratégie de groupe ne l'impose) ;
- sur les machines Modern Standby, le **délai de mise en veille sur batterie**
  passe à *Jamais*, car Windows interrompt sinon les demandes de maintien en
  éveil cinq minutes après ce délai sur batterie.

Elle ne touche jamais au délai d'extinction de l'écran, au délai de veille sur
secteur, au bouton d'alimentation, à la batterie critique, à la veille prolongée
ni au mode d'alimentation actif.

Les réglages sont rétablis à la fin du minuteur, sur **Arrêter**, à la fermeture
de Vigie, à la fermeture de session Windows, sur exception non gérée, et — si le
processus est tué — par un processus témoin, ou à défaut au lancement suivant ou
à l'ouverture de session suivante.

Si une session a été interrompue sans rétablir les réglages, lancez :

```powershell
Vigie.exe --restore
```

En dernier recours, vous pouvez rétablir l'action du capot à la main avec
`powercfg`, à partir des valeurs d'origine de `restore.json` (action du capot :
0 ne rien faire, 1 veille, 2 veille prolongée, 3 arrêter ; délai de veille en
secondes, 0 = jamais) :

```powershell
powercfg /setacvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION 1
powercfg /setdcvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION 1
powercfg /setdcvalueindex SCHEME_CURRENT SUB_SLEEP STANDBYIDLE 1200
powercfg /setactive SCHEME_CURRENT
```

## Désinstallation

1. Si une session a été interrompue, lancez `Vigie.exe --restore` une fois.
2. Supprimez le dossier de l'application et `%LOCALAPPDATA%\Vigie`.

Vigie n'installe aucun service, pilote ou tâche planifiée. La seule entrée de
démarrage utilisée est une valeur `RunOnce` sous `HKCU`, créée au début d'une
session et supprimée une fois les réglages rétablis.

## Confidentialité

Vigie n'émet aucune requête réseau et n'a aucune télémétrie. Les journaux et les
sorties de Claude Code restent dans `%LOCALAPPDATA%\Vigie` et sont supprimés
après 14 jours. Ces sorties peuvent contenir du code et des secrets de votre
projet ; elles restent locales. Vigie ne lit ni ne stocke les identifiants de
Claude Code.

## Transparence

En dehors des tâches Claude Code visibles dans l'interface, le seul processus
sans fenêtre lancé par Vigie est le processus témoin de restauration, et la seule
entrée de démarrage est la valeur `RunOnce` décrite ci-dessus. Les deux existent
pour que les réglages soient rétablis si Vigie est tuée. Aucune obfuscation,
aucune compression d'exécutable, aucune injection de processus.

Projet indépendant, non affilié à Anthropic. Claude et Claude Code sont des
marques d'Anthropic.

## Licence

[MIT](LICENSE).
