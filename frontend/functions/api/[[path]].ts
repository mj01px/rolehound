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

export async function onRequest({ request, env }: Context): Promise<Response> {
  if (!env.API_ORIGIN || !env.PROXY_SECRET) {
    return new Response('API proxy is not configured.', { status: 500 })
  }

  const incoming = new URL(request.url)
  const target = new URL(incoming.pathname.replace(/^\/api/, '') + incoming.search, env.API_ORIGIN)

  const proxied = new Request(target.toString(), request)
  // Caddy accepts only requests that carry this secret, and trusts the visitor address only from them.
  proxied.headers.set('X-Rolehound-Proxy', env.PROXY_SECRET)
  proxied.headers.set('X-Rolehound-Client-Ip', request.headers.get('CF-Connecting-IP') ?? '')

  return fetch(proxied, { redirect: 'manual' })
}
