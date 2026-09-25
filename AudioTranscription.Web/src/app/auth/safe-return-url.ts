/**
 * Only internal app paths are valid targets after login. Anything else (absolute URLs,
 * protocol-relative "//host", backslash tricks, the login page itself) falls back to "/"
 * so the returnUrl query parameter cannot be abused as an open redirect.
 */
export function safeReturnUrl(url: string | null | undefined): string {
  if (
    !url ||
    !url.startsWith('/') ||
    url.startsWith('//') ||
    url.startsWith('/\\') ||
    url.startsWith('/login')
  ) {
    return '/';
  }
  return url;
}
