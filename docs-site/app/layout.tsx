import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: {
    default: "Windows Server Setupper",
    template: "%s · Windows Server Setupper",
  },
  description:
    "Documentation, verified downloads, and local-only tools for Windows Server Setupper.",
  applicationName: "Windows Server Setupper",
  icons: {
    icon: "/brand/windows-server-setupper-logo.png",
    shortcut: "/brand/windows-server-setupper-logo.png",
  },
  keywords: [
    "Windows Server",
    "server setup",
    "resilient recovery",
    "Exchange installer",
  ],
  openGraph: {
    title: "Windows Server Setupper",
    description:
      "Server setup that preserves completed work and reports uncertain outcomes honestly.",
    type: "website",
  },
  twitter: {
    card: "summary",
    title: "Windows Server Setupper",
    description:
      "Documentation, verified downloads, and local-only companion tools.",
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en" suppressHydrationWarning>
      <body>{children}</body>
    </html>
  );
}
