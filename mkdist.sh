#!/bin/sh
GIT_REV=`git rev-parse --short=8 HEAD`
GIT_BRANCH=`git rev-parse --symbolic-full-name --abbrev-ref HEAD`
CUR_DATE=`date --rfc-3339='date'`
LAST_COMMIT_DATE=`git log -1 --format=%cs`

# The contents of .version can be seen inworld in Help->About Firestorm
# Extended info including the branch, git revision and last commit date:
echo "${GIT_BRANCH}@${GIT_REV} (${LAST_COMMIT_DATE})" > bin/.version

EXCLUDES="*Tests* *Gloebit* *OpenSimMutelist* *MoneyServer* *OpenSim.Data.MySQL.MySQLMoneyDataWrapper* *OpenSim.Modules.Currency* *server_cert.p12* *SineWaveCert.pfx*"
TARGETZIP=opensim-${GIT_BRANCH}-${LAST_COMMIT_DATE}_${GIT_REV}.zip
echo "${TARGETZIP}"
zip -r -o ${TARGETZIP} bin CONTRIBUTORS.txt LICENSE.txt README.md ThirdPartyLicenses helper/index.html helper/robots.txt helper/search extra/search -x ${EXCLUDES}

#Make seperate zip for Gloebit addon:
if [ -f bin/Gloebit.dll ]; then
    TARGETZIP=gloebit-${GIT_BRANCH}-${LAST_COMMIT_DATE}_${GIT_REV}.zip
    EXCLUDES="*Tests*"
    echo "${TARGETZIP}"
    cp addon-modules/Gloebit/GloebitMoneyModule/Gloebit.ini.example bin/Gloebit.ini.example
    zip -r -o ${TARGETZIP} bin/Gloebit.* -x ${EXCLUDES}
fi

#Make seperate zip for opensim.currency:
if [ -f bin/MoneyServer.dll ]; then
    TARGETZIP=opensimcurrency-${GIT_BRANCH}-${LAST_COMMIT_DATE}_${GIT_REV}.zip
    EXCLUDES="*Tests*"
    echo "${TARGETZIP}"
    cp addon-modules/opensim.currency/OpenSim.Grid.MoneyServer/MoneyServer.exe.config bin
    cp addon-modules/opensim.currency/OpenSim.Grid.MoneyServer/MoneyServer.ini.example bin
    cp addon-modules/opensim.currency/OpenSim.Grid.MoneyServer/server_cert.p12 bin
    cp addon-modules/opensim.currency/OpenSim.Grid.MoneyServer/SineWaveCert.pfx bin
    cp addon-modules/opensim.currency/config/OpenSim.ini.sample bin/OpenSim.ini-currency.example
    zip -r -o ${TARGETZIP} bin/MoneyServer.* bin/OpenSim.Data.MySQL.MySQLMoneyDataWrapper.* bin/OpenSim.Modules.Currency.* bin/server_cert.p12 bin/SineWaveCert.pfx bin/OpenSim.ini-currency.example helper/economy -x ${EXCLUDES}
fi

