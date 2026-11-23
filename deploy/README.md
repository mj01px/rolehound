# Deploying Rolehound

One Google Cloud e2-micro VM runs the API, PostgreSQL, Caddy (HTTPS), the task
scheduler and daily backups (`compose.yaml`). The web app runs on Cloudflare
Pages. CI builds the API image and pushes it to the GitHub Container Registry,
so the VM never compiles .NET.

```
browser ── app.example.com (Cloudflare Pages)
   └────── api.example.com ── Caddy ── API ── PostgreSQL
                                        └── scheduler, backup (same VM)
```

Replace `example.com` with your domain throughout.

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

```bash
gcloud compute ssh rolehound --zone=us-central1-a

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

**Email.** Create a Brevo account, verify the sender address, and use the SMTP
login and key from Brevo › SMTP & API. Port 2525.

**Telegram (optional).** Talk to @BotFather, send `/newbot`, and put the token
and the bot's username in `.env`. `Telegram__WebhookSecret` is any random value
of letters, digits, `_` and `-`. The API registers the webhook when it starts.

## 4. DNS and first start

1. After CI has pushed the image once, make it public so the VM can pull it
   without credentials: GitHub › Packages › `rolehound-api` › Package settings ›
   Change visibility › Public. The code is already public, so nothing new is exposed.
2. Point `api.example.com` (A record) to the VM's static address.
3. Start everything; Caddy obtains the HTTPS certificate on first request:

   ```bash
   docker compose pull
   docker compose up -d
   docker compose ps
   curl -s https://api.example.com/health/ready
   ```

4. The first admin (`Admin__Email`) receives an access link by email.

## 5. Web app on Cloudflare Pages

Create a Pages project from the GitHub repository:

| Setting | Value |
|---|---|
| Root directory | `frontend` |
| Build command | `npm ci && npm run build` |
| Output directory | `dist` |
| Environment variable | `VITE_API_URL=https://api.example.com` |

Node comes from `frontend/.nvmrc`. Add the custom domain `app.example.com`.
`Web__BaseUrl` in `.env` must be exactly that address (CORS and email links).
After the first deploy, open a deep link such as `https://app.example.com/funil`
directly to confirm Pages serves the app for every path.

## Updating

```bash
cd ~/rolehound && git pull
cd deploy && docker compose pull && docker compose up -d
```

Migrations run when the API starts. To pin a version, set `API_TAG` in `.env`
to a commit SHA that CI built.

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
database, so also copy one off the VM now and then:

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
curl -s https://api.example.com/health/ready
```
