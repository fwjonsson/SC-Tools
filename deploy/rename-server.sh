#!/bin/bash
# The one-time move of the server from the names before the rename (Hauling Tracker: haulingtracker) to SC Tools (sctools). Run it as root on the server
# before the pull request of the rename is merged (that one deploys to the new places):
#   curl -fsSL https://raw.githubusercontent.com/fwjonsson/SC-Tools/claude/rename-sc-tools/deploy/rename-server.sh -o rename.sh && bash rename.sh
# It stops the app, keeps a copy of the database in /root, moves the user, the folders, the settings, the service and the update script to the new names
# (the service, the update script and the right to restart it are made by setup.sh, as on a new server), and starts the app again: the version that runs
# now, until GitHub puts the new one in place. Every step looks at what is done already, so a run that stopped halfway is finished by running it again.
set -euo pipefail
BRANCH="${BRANCH:-claude/rename-sc-tools}"
OLD="Hauling""Tracker__"   # the settings' names before the rename (written in two parts, so a rename of the code never changes them)
NEW="SCTools__"

if [ "$(id -u)" -ne 0 ]; then echo "Run it as root."; exit 1; fi
if [ ! -d /opt/haulingtracker ] && [ ! -d /opt/sctools ]; then echo "There is neither /opt/haulingtracker nor /opt/sctools: nothing to move."; exit 1; fi
if [ -d /opt/sctools ] && [ -f /etc/systemd/system/sctools.service ] && systemctl is-active --quiet sctools; then
  echo "The server has the new names already and the app runs: nothing to do."
  exit 0
fi

echo "== setup.sh of the new names"
setup=/root/sctools-setup.sh
curl -fsSL "https://raw.githubusercontent.com/fwjonsson/SC-Tools/$BRANCH/deploy/setup.sh" -o "$setup" \
  || curl -fsSL "https://raw.githubusercontent.com/fwjonsson/SCHaulingTracker/$BRANCH/deploy/setup.sh" -o "$setup"
grep -q 'sctools.service' "$setup" || { echo "That is not the setup.sh of the new names."; exit 1; }

if [ -d /opt/haulingtracker ]; then
  echo "== stop the app, and a copy of the database"
  systemctl stop haulingtracker || true
  stamp=$(date +%Y%m%d-%H%M%S)
  for f in /var/lib/haulingtracker/tracker.db*; do cp -a "$f" "/root/before-rename-$stamp-$(basename "$f")"; done
  echo "   in /root: before-rename-$stamp-tracker.db"
fi

echo "== the user"
if id haulingtracker &>/dev/null; then
  usermod -l sctools haulingtracker
fi
if getent group haulingtracker >/dev/null; then groupmod -n sctools haulingtracker; fi
usermod -d /var/lib/sctools sctools

echo "== the folders"
[ -d /var/lib/haulingtracker ] && mv /var/lib/haulingtracker /var/lib/sctools
[ -d /opt/haulingtracker ] && mv /opt/haulingtracker /opt/sctools
[ -d /etc/haulingtracker ] && mv /etc/haulingtracker /etc/sctools
[ -f /etc/sctools/haulingtracker.env ] && mv /etc/sctools/haulingtracker.env /etc/sctools/sctools.env
cur="$(basename "$(readlink /opt/sctools/current)")"
ln -sfn "/opt/sctools/releases/$cur" /opt/sctools/current
chown -h deploy:deploy /opt/sctools/current
# the versions from before the rename are HaulingTracker.Server: they get the new name too (a link), so the service starts the one that runs now
for d in /opt/sctools/releases/*/; do
  if [ -e "${d}HaulingTracker.Server" ] && [ ! -e "${d}SCTools.Server" ]; then
    ln -s HaulingTracker.Server "${d}SCTools.Server"
    chown -h deploy:deploy "${d}SCTools.Server"
  fi
done

echo "== the settings"
env=/etc/sctools/sctools.env
sed -i 's#/var/lib/haulingtracker#/var/lib/sctools#g; s#restart haulingtracker#restart sctools#g; s#Hauling Tracker web app#SC Tools web app#g' "$env"
# the new version reads SCTools__..., the one that runs until GitHub puts it in place reads the old names: both are there until then
if ! grep -q "^$NEW" "$env"; then
  grep -q "^# the same settings under the new name" "$env" || { echo; echo "# the same settings under the new name (the new version reads these)"; } >> "$env"
  grep "^$OLD" "$env" | sed "s/^$OLD/$NEW/" >> "$env"
fi
grep -q "^${NEW}Database=" "$env" || { echo "The settings have no ${NEW}Database line: look at $env."; exit 1; }

echo "== the service, the update script and the right to restart it"
systemctl disable haulingtracker >/dev/null 2>&1 || true
rm -f /etc/systemd/system/haulingtracker.service /etc/sudoers.d/haulingtracker /usr/local/bin/haulingtracker-install
SETUP_QUIET=1 bash "$setup"
rm -f "$setup"

echo "== start the app"
systemctl restart sctools
for i in $(seq 1 30); do
  if curl -fs -o /dev/null http://127.0.0.1:8780/; then
    echo
    echo "Done: the app runs as the service sctools (systemctl status sctools). Now merge the pull request of the rename on GitHub."
    exit 0
  fi
  sleep 2
done
echo "The app does not answer. Its log:"
journalctl -u sctools -n 60 --no-pager || true
exit 1
