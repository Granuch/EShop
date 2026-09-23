# Client flows

End-to-end sequences a client implements, with sequence diagrams. Each flow names the calls, what to poll, and how
long to expect:
- sign-up and login, including two-factor login, silent token refresh and logout;
- browse → basket → checkout → order → payment → paid order;
- cancel and refund;
- the main admin tasks (product edit, bulk actions, import and export, user management).

> **Outline only.** Written in stage S9, once every service file is complete. Until then, the building blocks are in
> [conventions.md §4](conventions.md#4-authentication) (auth) and
> [conventions.md §9](conventions.md#9-consistency-and-caching) (what arrives asynchronously).

**Verified at:** not yet verified.

---

**Version**: 0.1  
**Last Updated**: 2026-09-23
