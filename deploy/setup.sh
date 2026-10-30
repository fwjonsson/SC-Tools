#!/bin/bash
# One-time setup of the server for the SC Tools web app (Ubuntu, run as root):
#   curl -fsSL https://raw.githubusercontent.com/fwjonsson/SC-Tools/main/deploy/setup.sh -o setup.sh && bash setup.sh
#
# It makes
#   - the user "sctools" that runs the app (it cannot log in), and the user "deploy" that GitHub logs in as to put a new version in place;
#   - /opt/sctools (the versions of the app; "current" is the one that runs) and /var/lib/sctools (the database, the logs, the backups);
#   - /etc/sctools/sctools.env, the settings (the admin's name and first password are typed in there);
#   - the service "sctools" (systemd), and the right for "deploy" to restart it;
#   - the key GitHub logs in with: it prints what goes in the GitHub secrets.
# Running it again changes nothing that is there already (the settings file and the key are kept).
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then echo "Run it as root."; exit 1; fi

echo "== users"
id sctools &>/dev/null || useradd --system --home-dir /var/lib/sctools --shell /usr/sbin/nologin sctools
id deploy &>/dev/null || useradd --create-home --shell /bin/bash deploy
usermod -aG systemd-journal deploy   # so a failed update can show the log of the service

echo "== folders"
install -d -o deploy -g deploy -m 755 /opt/sctools /opt/sctools/releases /opt/sctools/incoming
install -d -o sctools -g sctools -m 750 /var/lib/sctools
install -d -o root -g sctools -m 750 /etc/sctools

echo "== settings"
if [ ! -f /etc/sctools/sctools.env ]; then
  cat > /etc/sctools/sctools.env <<'EOF'
# The settings of the SC Tools web app (read when the service starts: sudo systemctl restart sctools after a change).
ASPNETCORE_URLS=http://127.0.0.1:8780
ASPNETCORE_ENVIRONMENT=Production
TZ=Europe/Stockholm
DOTNET_CLI_TELEMETRY_OPTOUT=1

# where the app keeps its data
SCTools__Database=/var/lib/sctools/tracker.db
SCTools__BackupDir=/var/lib/sctools/backups
SCTools__LogDir=/var/lib/sctools/logs
# the program's data file: it goes into the database the first time the app starts (copy it here before that)
SCTools__DataFile=/var/lib/sctools/data.json

# the admin: made the first time the app starts (at least 12 characters). Afterwards the name and the password are changed on the Account page,
# and the password can be removed from here. A forgotten password: set it here again, add SCTools__Admin__ResetPassword=true, restart, remove it.
SCTools__Admin__UserName=zzoega
SCTools__Admin__Password=
EOF
  chown root:sctools /etc/sctools/sctools.env
  chmod 640 /etc/sctools/sctools.env
fi

echo "== the update script"
cat > /usr/local/bin/sctools-install <<'EOF'
#!/bin/bash
# sctools-install <app.tgz>: unpacks a new version of the app next to the old ones, makes it the current one and restarts the service. When the
# new version does not answer within a minute, the version before it is put back. The newest 5 versions are kept. (GitHub runs it as "deploy".)
set -euo pipefail
tgz="$1"
root=/opt/sctools
new="$root/releases/$(date +%Y%m%d-%H%M%S)"
old="$(readlink -f "$root/current" 2>/dev/null || true)"
mkdir -p "$new"
tar -xzf "$tgz" -C "$new"
rm -f "$tgz"
chmod +x "$new/SCTools.Server"
ln -sfn "$new" "$root/current"
sudo /usr/bin/systemctl restart sctools
for i in $(seq 1 30); do
  if curl -fs -o /dev/null http://127.0.0.1:8780/; then   # the landing page answers whatever tools are shown
    echo "The new version runs: $new"
    ls -1dt "$root"/releases/*/ | tail -n +6 | xargs -r rm -rf
    exit 0
  fi
  sleep 2
done
echo "The new version does not answer. Its log:"
journalctl -u sctools -n 60 --no-pager || true
if [ -n "$old" ] && [ -d "$old" ]; then
  echo "The version before it is put back: $old"
  ln -sfn "$old" "$root/current"
  sudo /usr/bin/systemctl restart sctools
fi
exit 1
EOF
chmod 755 /usr/local/bin/sctools-install

echo "== the service"
cat > /etc/systemd/system/sctools.service <<'EOF'
[Unit]
Description=SC Tools web app
After=network-online.target
Wants=network-online.target

[Service]
User=sctools
Group=sctools
WorkingDirectory=/opt/sctools/current
ExecStart=/opt/sctools/current/SCTools.Server
EnvironmentFile=/etc/sctools/sctools.env
Restart=always
RestartSec=5
TimeoutStopSec=30
# it may only write its own data
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true
ReadWritePaths=/var/lib/sctools

[Install]
WantedBy=multi-user.target
EOF
cat > /etc/sudoers.d/sctools <<'EOF'
deploy ALL=(root) NOPASSWD: /usr/bin/systemctl restart sctools, /usr/bin/systemctl start sctools, /usr/bin/systemctl stop sctools
EOF
chmod 440 /etc/sudoers.d/sctools
visudo -cf /etc/sudoers.d/sctools >/dev/null
systemctl daemon-reload
systemctl enable sctools >/dev/null 2>&1   # it starts with the first version GitHub puts in place

echo "== the key GitHub logs in with"
install -d -o deploy -g deploy -m 700 /home/deploy/.ssh
if [ ! -f /root/github-deploy.pub ]; then
  ssh-keygen -q -t ed25519 -N "" -C "github-deploy" -f /root/github-deploy
fi
grep -qF "$(cat /root/github-deploy.pub)" /home/deploy/.ssh/authorized_keys 2>/dev/null || cat /root/github-deploy.pub >> /home/deploy/.ssh/authorized_keys
chown deploy:deploy /home/deploy/.ssh/authorized_keys
chmod 600 /home/deploy/.ssh/authorized_keys

ip="$(curl -4 -fs https://api.ipify.org || hostname -I | awk '{print $1}')"
echo
echo "Done. In GitHub: the repository, Settings, Secrets and variables, Actions, New repository secret, three times:"
echo
echo "DEPLOY_HOST:"
echo "$ip"
echo
echo "DEPLOY_KNOWN_HOSTS:"
ssh-keyscan -t ed25519 localhost 2>/dev/null | sed "s/^localhost/$ip/"
echo
if [ -f /root/github-deploy ]; then
  echo "DEPLOY_KEY (all the lines, from BEGIN to END; afterwards delete this copy: rm /root/github-deploy):"
  cat /root/github-deploy
else
  echo "DEPLOY_KEY: in GitHub already (its copy here was deleted). For a new one: rm /root/github-deploy.pub, run this again, and replace the secret."
fi
echo
echo "Then: type the admin's first password in /etc/sctools/sctools.env (nano /etc/sctools/sctools.env),"
echo "copy the program's data file to /var/lib/sctools/data.json, and run the Deploy workflow on GitHub."
