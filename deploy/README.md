# Deploying Rolehound

One Google Cloud e2-micro VM runs the API, PostgreSQL, Caddy (HTTPS), the task
scheduler and daily backups (`compose.yaml`). The web app runs on Cloudflare
Pages. CI builds the API image and pushes it to the GitHub Container Registry,
so the VM never compiles .NET.

No custom domain is needed. The browser only talks to the Pages site; a Pages
Function forwards `/api/*` to the VM, so the session cookie stays first party.
The VM gets a free DuckDNS name for its HTTPS certificate.

```
browser ── rolehound.pages.dev (Cloudflare Pages)
              └── /api/* (Pages Function, shared secret)
                    └── rolehound-api.duckdns.org ── Caddy ── API ── PostgreSQL
                                                             └── scheduler, backup (same VM)
Telegram ──────────── rolehound-api.duckdns.org/telegram/webhook
```

Replace `rolehound.pages.dev` and `rolehound-api.duckdns.org` with your names
throughout.

## 1. Google Cloud

1. Create a billing account and a project.
2. **Before creating anything**, add a budget: Billing › Budgets & alerts ›
   Create budget, amount US$1, alerts at 50%, 90% and 100%. Expected cost is the
   external IPv4 address only (about US$3.65 a month), covered by the Google AI
   Pro credits.
3. Create the VM (Free Tier needs `e2-micro` in `us-west1`, `us-central1` or
   `us-east1`, and a standard persistent disk of at most 30 GB):

   ```bash
   gcloud compute instances create rolehound \
     --zone=us-central1-a --machine-type=e2-micro \
     --image-family=ubuntu-2404-lts-amd64 --image-project=ubuntu-os-cloud \
     --boot-disk-size=30GB --boot-disk-type=pd-standard \
     --tags=web
   gcloud compute firewall-rules create rolehound-web \
     --allow=tcp:80,tcp:443 --target-tags=web
   ```

4. Reserve the VM's external address as static (VPC network › IP addresses),
   so it survives restarts.

## 2. The VM

SSH goes through Identity-Aware Proxy only: port 22 is closed to the internet
and open to Google's IAP range, and `gcloud` asks for your Google account.

```bash
gcloud compute firewall-rules create rolehound-ssh-iap \
  --allow=tcp:22 --source-ranges=35.235.240.0/20
gcloud compute firewall-rules delete default-allow-ssh default-allow-rdp
gcloud compute ssh rolehound --zone=us-central1-a --tunnel-through-iap

# 2 GB of swap: 1 GB of RAM is tight when a PDF is being rendered.
sudo fallocate -l 2G /swapfile && sudo chmod 600 /swapfile
sudo mkswap /swapfile && sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
```

Install Docker Engine and the Compose plugin following the official guide for
Ubuntu: https://docs.docker.com/engine/install/ubuntu/

```bash
sudo usermod -aG docker $USER   # log out and back in
git clone https://github.com/mj01px/rolehound.git
cd rolehound/deploy
cp .env.example .env && chmod 600 .env
```

## 3. Secrets in `deploy/.env`

Fill every value in `.env` (it is in `.gitignore`). Random values:

```bash
openssl rand -base64 48 | tr -d '/+=' | cut -c1-48
```

**Data Protection certificate.** It encrypts the OpenRouter keys and the login
cookies. Keep the `.pfx` and its password somewhere safe outside the VM: losing
them means every person has to sign in again and re-enter their OpenRouter key.

```bash
openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 -nodes \
  -keyout dp.key -out dp.crt -subj "/CN=rolehound-data-protection"
openssl pkcs12 -export -inkey dp.key -in dp.crt -out dp.pfx -passout pass:CHOOSE_A_PASSWORD
base64 -w0 dp.pfx   # value of DataProtection__CertificateBase64
rm dp.key dp.crt    # keep dp.pfx outside the VM
```

**Proxy secret.** `PROXY_SECRET` is a random value (command above). The same
value goes into the Cloudflare Pages project (section 5).

**Email.** Use a Gmail account made for the app. Turn on 2-Step Verification,
create an app password (Google Account › Security › App passwords), and fill
`Email__SmtpUser` and `Email__From` with the address and `Email__SmtpPassword`
with the app password. Host `smtp.gmail.com`, port 587 (STARTTLS); Google
Cloud blocks only port 25.

**Telegram (optional).** Talk to @BotFather, send `/newbot`, and put the token
and the bot's username in `.env`. `Telegram__WebhookSecret` is any random value
of letters, digits, `_` and `-`. The API registers the webhook when it starts.

## 4. DNS and first start

1. After CI has pushed the image once, make it public so the VM can pull it
   without credentials: GitHub › Packages › `rolehound-api` › Package settings ›
   Change visibility › Public. The code is already public, so nothing new is exposed.
2. Sign in at https://www.duckdns.org, create the subdomain (for example
   `rolehound-api`) and set its address to the VM's static address.
   `duckdns.org` is on the Public Suffix List, so Let's Encrypt treats each
   subdomain as its own domain.
3. Set `API_TAG` in `.env` to the full SHA of the commit CI published (GitHub ›
   Actions › the run's "Publish API image" job). The API runs exactly that build.
4. Start everything; Caddy obtains the HTTPS certificate on first request:

   ```bash
   docker compose pull
   docker compose build scheduler
   docker compose up -d
   docker compose ps
   curl -s https://rolehound-api.duckdns.org/health/ready
   ```

   Any other path called directly returns 404: only the Pages proxy may reach
   the API.

5. The first admin (`Admin__Email`) receives an access link by email.

## 5. Web app on Cloudflare Pages

Create a Pages project from the GitHub repository:

| Setting | Value |
|---|---|
| Root directory | `frontend` |
| Build command | `npm ci && npm run build` |
| Output directory | `dist` |
| Variable | `API_ORIGIN=https://rolehound-api.duckdns.org` |
| Secret | `PROXY_SECRET`, the same value as in `.env` (type Secret) |

Do not set `VITE_API_URL`: the app calls `/api` on its own site, and
`frontend/functions/api/[[path]].ts` forwards it to the VM. Node comes from
`frontend/.nvmrc`. `Web__BaseUrl` in `.env` must be exactly the Pages address
(CORS and email links). Pages Functions share the Workers Free quota of
100,000 requests a day; static files do not count.

After the first deploy, open a deep link such as
`https://rolehound.pages.dev/funil` directly to confirm Pages serves the app
for every path, then sign in to confirm the proxy and the session cookie.

## Updating

Wait for CI's "Publish API image" job on the commit, then on the VM:

```bash
cd ~/rolehound && git pull
cd deploy
sed -i "s/^API_TAG=.*/API_TAG=$(git rev-parse HEAD)/" .env
docker compose pull && docker compose build scheduler && docker compose up -d
```

Migrations run when the API starts. To roll back, set `API_TAG` to the previous
SHA and run `docker compose up -d` again (a migration that already ran stays).

## Scheduled tasks

The `scheduler` service (`scheduler/crontab`, America/Sao_Paulo):

| When | Task | What it does |
|---|---|---|
| Every 15 minutes | `/tasks/collect-due` | Collects when the 09:00, 12:00, 15:00 or 18:00 slot has no run yet (catch-up included) |
| 09:00 | `/tasks/notify-overdue` | Telegram digest of overdue follow-ups, once a day |
| 03:30 | `/tasks/cleanup` | Retention: security log, spent tokens, old jobs and tombstones |

Logs: `docker compose logs scheduler`.

## Backups

The `backup` service writes `rolehound-YYYY-MM-DD.dump` at 04:00 into the
`backups` volume and keeps 7 days. These copies live on the same disk as the
database, so also copy one off the VM now and then. A deleted account stays in
these dumps until they rotate out, 7 days at most:

```bash
docker compose cp backup:/backups ./backups-copy
```

Restore into an empty database:

```bash
docker compose exec -T postgres pg_restore -U rolehound -d rolehound --clean --if-exists < rolehound-YYYY-MM-DD.dump
```

## Checking health

```bash
docker compose ps                        # all services healthy
docker stats --no-stream                 # memory per container
curl -s https://rolehound-api.duckdns.org/health/ready
```
