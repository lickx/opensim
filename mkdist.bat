@echo off
SET VERSION_FILE=bin\.version

FOR /F "usebackq tokens=*" %%F IN (`"git rev-parse --short=8 HEAD"`) DO (
SET GIT_REV=%%F
)
FOR /F "usebackq tokens=*" %%F IN (`"git rev-parse --symbolic-full-name --abbrev-ref HEAD"`) DO (
SET GIT_BRANCH=%%F
)
FOR /F "usebackq tokens=*" %%F IN (`"git log -1 --format=%%cs"`) DO (
SET LAST_COMMIT_DATE=%%F
)

REM The contents of .version can be seen inworld in Help->About Firestorm
REM Extended info including the branch, git revision and last commit date:
echo %GIT_BRANCH%@%GIT_REV% (%LAST_COMMIT_DATE%) > %VERSION_FILE%

SET TARGET_ZIP=opensim-%GIT_BRANCH%-%LAST_COMMIT_DATE%_%GIT_REV%.zip
echo %TARGET_ZIP%
set excludes=-x "*Tests*" "bin/Gloebit.*" "bin/OpenSimMutelist.Modules.*" "bin/MoneyServer.*" "bin/OpenSim.Data.MySQL.MySQLMoneyDataWrapper.*" "bin/OpenSim.Modules.Currency.*" "bin/server_cert.p12" "bin/SineWaveCert.pfx"
zip -r -o "%TARGET_ZIP%" bin CONTRIBUTORS.txt LICENSE.txt README.md ThirdPartyLicenses helper/index.html helper/robots.txt helper/search extra/OpenSimSearch %excludes%

if not exist bin\Gloebit.dll goto skipgloebit
SET TARGET_ZIP=gloebit-%GIT_BRANCH%-%LAST_COMMIT_DATE%_%GIT_REV%.zip
set excludes=-x "*Tests*"
echo %TARGET_ZIP%
copy addon-modules\Gloebit\GloebitMoneyModule\Gloebit.ini.example bin\Gloebit.ini.example
zip -r -o "%TARGET_ZIP%" bin/Gloebit.* %excludes%
:skipgloebit

if not exist bin\MoneyServer.dll goto skipopensimcurrency
SET TARGET_ZIP=opensimcurrency-%GIT_BRANCH%-%LAST_COMMIT_DATE%_%GIT_REV%.zip
set excludes=-x "*Tests*"
echo %TARGET_ZIP%
copy addon-modules\opensim.currency\OpenSim.Grid.MoneyServer\MoneyServer.exe.config bin
copy addon-modules\opensim.currency\OpenSim.Grid.MoneyServer\MoneyServer.ini.example bin
copy addon-modules\opensim.currency\OpenSim.Grid.MoneyServer\server_cert.p12 bin
copy addon-modules\opensim.currency\OpenSim.Grid.MoneyServer\SineWaveCert.pfx bin
copy addon-modules\opensim.currency\config\OpenSim.ini.sample bin\OpenSim.ini-currency.example
zip -r -o "%TARGET_ZIP%" bin/MoneyServer.* bin/OpenSim.Data.MySQL.MySQLMoneyDataWrapper.* bin/OpenSim.Modules.Currency.* bin/server_cert.p12 bin/SineWaveCert.pfx bin/OpenSim.ini-currency.example helper/economy %excludes%
:skipopensimcurrency


:end
