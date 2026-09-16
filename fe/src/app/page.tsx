export default function HomePage() {
  return (
    <main className="flex min-h-screen items-center justify-center px-6 py-16">
      <section className="w-full max-w-2xl rounded-2xl border border-slate-200 bg-white p-8 shadow-sm sm:p-12">
        <p className="text-sm font-semibold tracking-widest text-sky-700">
          DRONEOPS INTELLIGENCE
        </p>
        <h1 className="mt-4 text-4xl font-semibold tracking-tight sm:text-5xl">
          Mission operations, connected.
        </h1>
        <p className="mt-6 text-lg leading-8 text-slate-600">
          Plan missions, monitor flights, review incidents and explore operational
          insights in one place.
        </p>
        <p className="mt-8 border-t border-slate-100 pt-6 text-sm text-slate-500">
          Application under development.
        </p>
      </section>
    </main>
  );
}
