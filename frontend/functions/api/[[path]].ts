// Cloudflare Pages Function: forwards /api/* to the API on the VM, so the browser talks to one site only
// and the SameSite=Strict session cookie keeps working without a custom domain.

interface Env {
  API_ORIGIN?: string
  PROXY_SECRET?: string
}

interface Context {
  request: Request
  env: Env
}

// Builds the upstream URL by setting only path and query on the configured origin. Resolving the incoming
// path as a relative URL would let "/api//other.host/x" become a scheme-relative URL and leave the API host.
function upstreamUrl(requestUrl: string, apiOrigin: string): URL | null {
  const incoming = new URL(requestUrl)
  const origin = new URL(apiOrigin)
  const target = new URL(origin.origin)
  target.pathname = incoming.pathname.replace(/^\/api(?=\/|$)/, '') || '/'
  target.search = incoming.search
  return target.origin === origin.origin ? target : null
}

export async function onRequest({ request, env }: Context): Promise<Response> {
  if (!env.API_ORIGIN || !env.PROXY_SECRET) {
    return new Response('API proxy is not configured.', { status: 500 })
  }

  const target = upstreamUrl(request.url, env.API_ORIGIN)
  if (!target) return new Response('Not found.', { status: 404 })

  const proxied = new Request(target.toString(), request)
  // Caddy accepts only requests that carry this secret, and trusts the visitor address only from them.
  proxied.headers.set('X-Rolehound-Proxy', env.PROXY_SECRET)
  proxied.headers.set('X-Rolehound-Client-Ip', request.headers.get('CF-Connecting-IP') ?? '')

  return fetch(proxied, { redirect: 'manual' })
}
